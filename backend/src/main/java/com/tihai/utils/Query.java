package com.tihai.utils;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.JsonNode;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import com.tihai.constant.QueryResponseConstant;
import com.tihai.constant.QuestionTypeConstant;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;
import org.springframework.web.reactive.function.client.WebClient;
import reactor.core.publisher.Mono;
import org.springframework.http.MediaType;

import java.io.IOException;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;

/**
 * @Copyright : DuanInnovator
 * @Description : 查题工具类
 * @Author : DuanInnovator
 * @CreateTime : 2025/4/24
 * @Link : <a href="https://github.com/DuanInnovator/TiHaiWuYou-Admin/tree/mine-admin">...</a>
 **/
@Component
@Slf4j
public class Query {

    @Autowired
    private WebClient tikuWebClient;

    @Autowired(required = false)
    private TiKuConfig tiKuConfig;

    @Autowired(required = false)
    private CxmoocQuestionBank cxmooc;

    @Autowired(required = false)
    private BuguakeQuestionBank buguake;

    @Autowired(required = false)
    private LocalQuestionBank localBank;


    /**
     * 同步查询题目返回答案
     *
     * @param question 题目信息
     * @return 答案
     * @throws IOException 异常
     */
    public String queryAnswerSync(Question question) throws IOException {
        if(localBank!=null&&tiKuConfig!=null&&tiKuConfig.getLocalBankFile()!=null&&!tiKuConfig.getLocalBankFile().trim().isEmpty()){
            try{String answer=localBank.queryAnswer(question);log.info("题库命中：本地题库");return answer;}
            catch(LocalQuestionBank.NoMatchException missing){/* Continue with the online providers. */}
            catch(LocalQuestionBank.ConflictException conflict){throw conflict;}
            catch(IOException unavailable){log.warn("本地题库未能读取，继续查询在线题库");}
        }
        boolean supplement = tiKuConfig != null && tiKuConfig.isBuguakeEnabled() && buguake != null
                && java.util.Arrays.asList("single", "multiple", "judgement").contains(question.getType());
        IOException primary;
        try {
            String answer = queryPrimaryAnswer(question);
            if (supplement) { QuizAnswers.resolve(question, answer); }
            log.info("题库命中：{}", tiKuConfig == null ? "原题库" : tiKuConfig.getProvider());
            return answer;
        } catch (IOException failure) {
            if (!supplement || Thread.currentThread().isInterrupted()) { throw failure; }
            primary = failure;
        }
        log.info("原题库未返回可匹配答案，正在补查不挂科");
        try {
            String answer = buguake.queryAnswer(question);
            QuizAnswers.resolve(question, answer);
            log.info("题库命中：不挂科");
            return answer;
        } catch (BuguakeQuestionBank.NoMatchException missing) {
            // Retain the existing distinction between a genuine miss and a provider failure.
            primary.addSuppressed(missing);
            throw primary;
        }
    }

    private String queryPrimaryAnswer(Question question) throws IOException {

        if (tiKuConfig != null && "cxmooc".equalsIgnoreCase(tiKuConfig.getProvider())) {
            if (cxmooc == null) { throw new IOException("cxmooc 题库尚未初始化"); }
            return cxmooc.queryAnswer(question);
        }

        Map<String, Object> requestBody = new HashMap<>();
        requestBody.put("question", question.getTitle());
        requestBody.put("options", question.getTKOptions());
        int type = 0;
        String questionType = question.getType();
        switch (questionType) {
            case "single":
                type = QuestionTypeConstant.SINGLE_CHOICE;
                break;
            case "multiple":
                type = QuestionTypeConstant.MULTIPLE_CHOICE;
                break;
            case "fill":
                type = QuestionTypeConstant.FILL_IN;
                break;
            case "judgement":
                type = QuestionTypeConstant.JUDGMENT;
                break;
            case "short":
                type = QuestionTypeConstant.SHORT_ANSWER;
                break;
            default:
                throw new IOException("不支持的题库查询题型；测验未提交");
        }
        requestBody.put("type", type);

        try {
            return tikuWebClient.post()
                .uri("")
                .bodyValue(requestBody)
                .exchangeToMono(response -> {
                    int status = response.rawStatusCode();
                    if (status >= 300 && status < 400) {
                        return response.releaseBody().then(Mono.<String>error(new IOException(
                                "题库接口已跳转（HTTP " + status + "），请检查题库服务地址；测验未提交")));
                    }
                    if (!response.statusCode().is2xxSuccessful()) {
                        return response.releaseBody().then(Mono.<String>error(new IOException(
                                "题库服务不可用（HTTP " + status + "）；测验未提交")));
                    }
                    MediaType contentType = response.headers().contentType().orElse(null);
                    if (contentType != null && MediaType.TEXT_HTML.isCompatibleWith(contentType)) {
                        return response.releaseBody().then(Mono.<String>error(new IOException(
                                "题库返回网页而非答案数据；测验未提交")));
                    }
                    return response.bodyToMono(String.class).defaultIfEmpty("").flatMap(raw -> {
                        try {
                            return Mono.just(parseAnswer(raw));
                        } catch (IOException failure) {
                            return Mono.error(failure);
                        }
                    });
                })
                .timeout(Duration.ofSeconds(10))
                .block();
        } catch (RuntimeException failure) {
            Throwable cause = reactor.core.Exceptions.unwrap(failure);
            if (cause instanceof IOException) { throw (IOException) cause; }
            throw new IOException("题库请求失败或超时；测验未提交", cause);
        }
    }

    static String parseAnswer(String raw) throws IOException {
        final JsonNode result;
        try {
            result = new ObjectMapper().readTree(raw);
        } catch (JsonProcessingException failure) {
            throw new IOException("题库返回的数据不是有效 JSON；测验未提交");
        }
        if (result == null || !result.isObject() || !result.has("code")
                || !result.get("code").isIntegralNumber() || !result.get("code").canConvertToInt()) {
            throw new IOException("题库返回格式不兼容（缺少有效状态码）；测验未提交");
        }
        int code = result.get("code").asInt();
        if (code != QueryResponseConstant.SUCCESS_CODE) {
            throw new IOException("题库未返回可用答案（业务状态码 " + code + "）；测验未提交");
        }
        JsonNode data = result.get("data");
        if (data == null || data.isNull() || (data.isTextual() && data.asText().trim().isEmpty())) {
            throw new IOException("题库未查到答案；测验未提交");
        }
        if (!data.isTextual()) {
            throw new IOException("题库答案格式不兼容；测验未提交");
        }
        return data.asText().trim();
    }

}

