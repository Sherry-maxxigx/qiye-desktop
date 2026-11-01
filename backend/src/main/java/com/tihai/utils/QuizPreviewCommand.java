package com.tihai.utils;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.springframework.web.reactive.function.client.WebClient;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** A query-only diagnostic entry point; it has no platform submission or database client. */
public final class QuizPreviewCommand {
    public static void main(String[] args) throws Exception {
        if (args.length != 2) { throw new IllegalArgumentException("Usage: quiz-page.html report.json"); }
        String html = new String(Files.readAllBytes(Paths.get(args[0])), StandardCharsets.UTF_8);
        List<Question> questions = CourseUtil.workQuestions(Decode.decodeQuestionsInfo(html));
        TiKuConfig config = new TiKuConfig();
        CxmoocQuestionBank bank = new CxmoocQuestionBank(WebClient.builder().build(), config);
        List<Map<String, Object>> rows = new ArrayList<>();
        int matched = 0;
        for (Question question : questions) {
            Map<String, Object> row = new LinkedHashMap<>();
            row.put("number", rows.size() + 1); row.put("title", question.getTitle());
            row.put("type", question.getType()); row.put("options", question.getTKOptions());
            try {
                String answer = bank.queryAnswer(question);
                row.put("answer", answer);
                row.put("matchedAnswer", QuizAnswers.resolve(question, answer));
                row.put("status", "matched"); matched++;
            } catch (java.io.IOException failure) {
                row.put("status", "unmatched"); row.put("reason", failure.getMessage());
            }
            rows.add(row);
            System.out.println("Quiz preview " + rows.size() + "/" + questions.size() + ": " + row.get("status"));
        }
        Map<String, Object> report = new LinkedHashMap<>();
        report.put("provider", "cxmooc"); report.put("submitted", false);
        report.put("total", questions.size()); report.put("matched", matched);
        report.put("allMatched", matched == questions.size()); report.put("questions", rows);
        new ObjectMapper().writerWithDefaultPrettyPrinter().writeValue(Paths.get(args[1]).toFile(), report);
        System.out.println("Quiz preview complete: " + matched + "/" + questions.size() + "; submitted=false");
    }
}
