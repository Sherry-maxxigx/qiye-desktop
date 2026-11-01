package com.tihai.utils;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.springframework.http.HttpHeaders;
import org.springframework.stereotype.Component;
import org.springframework.web.reactive.function.client.WebClient;
import reactor.core.publisher.Mono;

import java.io.IOException;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.*;

/** Public search supplement. Only identical stems, types and complete option sets may match. */
@Component
public class BuguakeQuestionBank {
    private static final ObjectMapper JSON = new ObjectMapper();
    private final WebClient client;

    public BuguakeQuestionBank(WebClient tikuWebClient, TiKuConfig config) {
        client = tikuWebClient.mutate().baseUrl(config.getBuguakeUrl())
                .defaultHeaders(headers -> { headers.remove(HttpHeaders.COOKIE); headers.remove(HttpHeaders.AUTHORIZATION); })
                .codecs(codecs -> codecs.defaultCodecs().maxInMemorySize(1024 * 1024)).build();
    }

    public String queryAnswer(Question question) throws IOException {
        if (Thread.currentThread().isInterrupted()) { throw new IOException("题库补查已中断；测验未提交"); }
        try {
            return client.get().uri(builder -> builder.queryParam("query", QuizAnswers.title(question.getTitle()))
                            .queryParam("rn", 10).queryParam("pn", 0).build())
                    .exchangeToMono(response -> {
                        if (!response.statusCode().is2xxSuccessful()) {
                            return response.releaseBody().then(Mono.<String>error(new IOException(
                                    "不挂科查询失败（HTTP " + response.rawStatusCode() + "）；测验未提交")));
                        }
                        return response.bodyToMono(String.class).defaultIfEmpty("").flatMap(raw -> {
                            try { return Mono.just(parseAnswer(raw, question)); }
                            catch (IOException failure) { return Mono.error(failure); }
                        });
                    }).timeout(Duration.ofSeconds(8)).block();
        } catch (RuntimeException failure) {
            Throwable cause = reactor.core.Exceptions.unwrap(failure);
            if (cause instanceof IOException) { throw (IOException) cause; }
            throw new IOException("不挂科查询连接失败或超时；测验未提交", cause);
        }
    }

    public static final class NoMatchException extends IOException {
        NoMatchException() { super("题库未查到答案（不挂科未匹配到同题、同选项）；测验未提交"); }
    }

    static String parseAnswer(String raw, Question question) throws IOException {
        final JsonNode response;
        try { response = JSON.readTree(raw); }
        catch (IOException failure) { throw new IOException("不挂科返回格式异常；测验未提交"); }
        if (response == null || !response.path("errno").isIntegralNumber() || response.path("errno").asInt() != 0) {
            throw new IOException("不挂科返回状态或格式异常；测验未提交");
        }
        // A successful search with no results uses data:[] instead of data:{list:[]}.
        if (response.path("data").isArray() && response.path("data").isEmpty()) { throw new NoMatchException(); }
        if (!response.path("data").path("list").isArray() || response.path("data").path("list").size() > 30) {
            throw new IOException("不挂科返回状态或格式异常；测验未提交");
        }
        Map<String, String> candidates = new LinkedHashMap<>();
        int decoded = 0;
        JsonNode rows = response.path("data").path("list");
        for (JsonNode row : rows) {
            final JsonNode item;
            try { item = decode(row.path("bdjson").asText(), row.path("actk").asText()); }
            catch (IOException failure) { continue; }
            decoded++;
            if (!stem(question.getTitle()).equals(stem(content(item.path("que_stem"))))) { continue; }
            JsonNode meta = item.path("que_info").path("meta");
            if (meta.path("img_que_stem").asBoolean() || meta.path("img_que_options").asBoolean()) { continue; }
            String sourceType = item.path("que_info").path("type").asText();
            String expected = "single".equals(question.getType()) ? "sc" : "multiple".equals(question.getType()) ? "mc"
                    : "judgement".equals(question.getType()) ? "tf" : "unsupported";
            if (!expected.equals(sourceType)) { continue; }
            try {
                String answer = "tf".equals(sourceType) ? content(item.path("que_answer")) : choiceAnswer(item, question);
                String selection = QuizAnswers.resolve(question, answer);
                candidates.put(selection, answer);
            } catch (IOException ignored) { }
        }
        if (!rows.isEmpty() && decoded == 0) { throw new IOException("不挂科题目解析失败；测验未提交"); }
        if (candidates.size() > 1) { throw new IOException("不挂科返回互相冲突的答案；测验未提交"); }
        if (candidates.isEmpty()) { throw new NoMatchException(); }
        return candidates.values().iterator().next();
    }

    private static String choiceAnswer(JsonNode item, Question question) throws IOException {
        List<String> texts = new ArrayList<>(), selected = new ArrayList<>();
        JsonNode groups = item.path("que_options");
        if (!groups.isArray() || groups.size() != 1 || !groups.get(0).isArray()) { throw new NoMatchException(); }
        for (JsonNode option : groups.get(0)) {
            String text = content(option.path("ret"));
            if (text.isEmpty() || text.contains("#") || !option.path("yes").isBoolean()) { throw new NoMatchException(); }
            texts.add(optionKey(text));
            if (option.path("yes").asBoolean()) { selected.add(text); }
        }
        List<String> current = new ArrayList<>();
        if (question.getTKOptions() == null) { throw new NoMatchException(); }
        for (String raw : question.getTKOptions()) {
            String label = QuizAnswers.text(raw);
            if (!label.matches("^[A-Z](?:[.、:：)）\\s].*|[^A-Za-z].*)$")) { throw new NoMatchException(); }
            current.add(optionKey(label.substring(1).replaceFirst("^[.、:：)）\\s]+", "")));
        }
        if (texts.size() < 2 || new HashSet<>(texts).size() != texts.size()
                || new HashSet<>(current).size() != current.size()) { throw new NoMatchException(); }
        Collections.sort(texts); Collections.sort(current);
        if (!texts.equals(current) || selected.isEmpty() || ("single".equals(question.getType()) && selected.size() != 1)) {
            throw new NoMatchException();
        }
        return String.join("#", selected);
    }

    private static String optionKey(String raw) { return QuizAnswers.text(raw).replaceAll("\\s+", ""); }

    private static String stem(String raw) {
        return QuizAnswers.comparableTitle(QuizAnswers.title(raw)
                .replaceFirst("^(?:选择题|单选题|多选题|判断题|填空题|简答题|问答题)\\s*[:：]\\s*", ""));
    }

    private static String content(JsonNode node) {
        if (node.isTextual() || node.isBoolean()) { return node.asText(); }
        if (node.isArray()) {
            StringBuilder result = new StringBuilder();
            for (JsonNode child : node) { result.append(content(child)); }
            return result.toString();
        }
        if (node.isObject()) { return content(node.path("c")); }
        return "";
    }

    // Public response format documented by tikuAdapter's internal/search/buguake.go.
    static JsonNode decode(String encoded, String salt) throws IOException {
        if (encoded.isEmpty() || encoded.length() > 700000 || salt.isEmpty() || salt.length() > 256) {
            throw new IOException("Invalid public question payload");
        }
        try {
            byte[] key = ("34cab29ef956d78afd" + salt).getBytes(StandardCharsets.UTF_8);
            byte[] bytes = Base64.getDecoder().decode(encoded);
            if (bytes.length % 2 != 0) { throw new IOException("Invalid public question payload"); }
            StringBuilder transformed = new StringBuilder();
            for (int i = 0; i < bytes.length; i += 2) {
                transformed.append((char) ((bytes[i] ^ key[i % key.length] ^ bytes[i + 1] ^ key[(i + 1) % key.length]) & 255));
            }
            JsonNode item = JSON.readTree(URLDecoder.decode(transformed.toString(), "UTF-8").replace('+', ' '));
            if (item == null || !item.isObject() || !item.path("que_stem").isArray()) { throw new IOException("Invalid public question payload"); }
            return item;
        } catch (IllegalArgumentException failure) { throw new IOException("Invalid public question payload"); }
    }
}
