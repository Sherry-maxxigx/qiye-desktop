package com.tihai.utils;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Component;
import org.springframework.web.reactive.function.BodyInserters;
import org.springframework.web.reactive.function.client.WebClient;
import reactor.core.publisher.Mono;
import java.io.IOException;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.ReentrantLock;

/** The legacy cxmooc protocol. One request at a time across every local task and preview. */
@Component
public class CxmoocQuestionBank {
    private final WebClient client;
    private final TiKuConfig config;
    private final ReentrantLock serial = new ReentrantLock();
    private long lastFinishedNanos;

    public CxmoocQuestionBank(WebClient tikuWebClient, TiKuConfig config) {
        this.config = config;
        this.client = tikuWebClient.mutate().baseUrl(config.getCxmoocUrl()).build();
    }

    public String queryAnswer(Question question) throws IOException {
        boolean choices = Arrays.asList("single", "multiple").contains(question.getType())
                && question.getTKOptions() != null && !question.getTKOptions().isEmpty();
        IOException mismatch = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            String answer = requestAnswer(question);
            if (!choices) { return answer; }
            try {
                QuizAnswers.resolve(question, answer);
                return answer;
            } catch (IOException failure) {
                // This service sometimes returns only a stored letter. Requery for actual
                // option text; never interpret the bank's letter as the current page's order.
                mismatch = failure;
            }
        }
        throw new IOException("题库重查 3 次仍无法匹配当前选项；测验未提交", mismatch);
    }

    private String requestAnswer(Question question) throws IOException {
        int type = type(question.getType());
        boolean acquired = false;
        try {
            serial.lockInterruptibly();
            acquired = true;
            long remaining = TimeUnit.MILLISECONDS.toNanos(1500) - (System.nanoTime() - lastFinishedNanos);
            if (remaining > 0) { TimeUnit.NANOSECONDS.sleep(remaining); }
            WebClient.RequestBodySpec request = client.post().uri("")
                    .contentType(MediaType.APPLICATION_FORM_URLENCODED).header("X-Version", "2.5")
                    .header("User-Agent", "Mozilla/5.0");
            if (config.getCxmoocToken() != null && !config.getCxmoocToken().trim().isEmpty()) {
                request.header("Authorization", config.getCxmoocToken());
            }
            return request.body(BodyInserters.fromFormData("info", "{}")
                            .with("topic[0]", QuizAnswers.title(question.getTitle())).with("type[0]", String.valueOf(type)))
                    .exchangeToMono(response -> {
                        if (!response.statusCode().is2xxSuccessful()) {
                            return response.releaseBody().then(Mono.<String>error(new IOException(
                                    "cxmooc 题库请求失败（HTTP " + response.rawStatusCode() + "）；测验未提交")));
                        }
                        return response.bodyToMono(String.class).defaultIfEmpty("").flatMap(raw -> {
                            try { return Mono.just(parseAnswer(raw, question)); }
                            catch (IOException failure) { return Mono.error(failure); }
                        });
                    }).timeout(Duration.ofSeconds(15)).block();
        } catch (InterruptedException interrupted) {
            Thread.currentThread().interrupt();
            throw new IOException("题库查询已中断；测验未提交");
        } catch (RuntimeException failure) {
            Throwable cause = reactor.core.Exceptions.unwrap(failure);
            if (cause instanceof IOException) { throw (IOException) cause; }
            throw new IOException("cxmooc 题库连接失败或超时；测验未提交", cause);
        } finally {
            if (acquired) { lastFinishedNanos = System.nanoTime(); serial.unlock(); }
        }
    }

    static int type(String type) throws IOException {
        switch (type) {
            case "single": return 1;
            case "multiple": return 2;
            case "judgement": return 3;
            case "fill": case "completion": return 4;
            default: throw new IOException("cxmooc 暂不支持该题型；测验未提交");
        }
    }

    static String parseAnswer(String raw, Question question) throws IOException {
        JsonNode response;
        try { response = new ObjectMapper().readTree(raw); }
        catch (IOException failure) { throw new IOException("cxmooc 返回的不是有效 JSON；测验未提交"); }
        if (response != null && response.isObject() && response.path("code").asInt() == -2) {
            throw new IOException("cxmooc 题库触发限流，请稍后再试；测验未提交");
        }
        if (response == null || !response.isArray()) {
            throw new IOException("cxmooc 题库返回格式不兼容；测验未提交");
        }
        Set<String> candidates = new HashSet<>();
        for (JsonNode row : response) {
            if (!row.path("index").isIntegralNumber() || row.path("index").asInt() != 0
                    || !row.path("result").isArray()) { continue; }
            for (JsonNode result : row.path("result")) {
                if (!result.path("type").isIntegralNumber() || result.path("type").asInt() != type(question.getType())
                        || !QuizAnswers.comparableTitle(question.getTitle()).equals(QuizAnswers.comparableTitle(result.path("topic").asText()))
                        || !result.path("correct").isArray() || result.path("correct").size() == 0) { continue; }
                List<String> answers = new ArrayList<>();
                boolean valid = true;
                for (JsonNode correct : result.path("correct")) {
                    JsonNode content = correct.get("content");
                    if (content == null || (!content.isTextual() && !content.isBoolean())) { valid = false; break; }
                    String text = QuizAnswers.text(content.asText());
                    if (text.isEmpty() || text.contains("#")) { valid = false; break; }
                    answers.add(text);
                }
                boolean blanks="fill".equals(question.getType()) || "completion".equals(question.getType());
                if (!"multiple".equals(question.getType()) && !blanks && answers.size() != 1) { valid = false; }
                if (valid) {
                    if ("multiple".equals(question.getType())) Collections.sort(answers);
                    candidates.add(String.join("#", answers));
                }
            }
        }
        if (candidates.isEmpty()) { throw new IOException("cxmooc 未查到题干、题型一致的答案；测验未提交"); }
        if (candidates.size() != 1) { throw new IOException("cxmooc 返回互相冲突的答案；测验未提交"); }
        return candidates.iterator().next();
    }
}
