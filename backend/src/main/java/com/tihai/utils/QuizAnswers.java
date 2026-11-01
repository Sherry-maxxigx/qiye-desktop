package com.tihai.utils;

import com.tihai.common.Question;
import org.jsoup.Jsoup;
import java.io.IOException;
import java.text.Normalizer;
import java.util.*;

/** Converts answer text to the current page's options, independently of bank option letters. */
public final class QuizAnswers {
    private QuizAnswers() { }

    static String text(String raw) {
        return Normalizer.normalize(Jsoup.parse(raw == null ? "" : raw).text(), Normalizer.Form.NFKC)
                .replace('\u00a0', ' ').trim();
    }

    public static String title(String raw) {
        return text(raw).replaceFirst("^\\d+\\s*(?=【|\\[)", "")
                .replaceFirst("^\\d+(?:\\s*、\\s*|\\.\\s+)", "")
                .replaceFirst("^(?:【|\\[)(?:单选题|多选题|判断题|填空题|简答题)(?:】|\\])\\s*", "").trim();
    }

    static String comparableTitle(String raw) {
        return title(raw).replaceAll("\\s+", "").replaceAll("[?？。]+$", "");
    }

    private static String comparableOption(String raw) {
        return text(raw).replaceAll("\\s+", "");
    }

    public static String resolve(Question question, String answer) throws IOException {
        if (answer == null || answer.trim().isEmpty()) { throw new IOException("题库未查到答案；测验未提交"); }
        if ("judgement".equals(question.getType())) {
            String normalized = text(answer).toLowerCase(Locale.ROOT);
            if (Arrays.asList("正确", "对", "是", "true", "√").contains(normalized)) { return "true"; }
            if (Arrays.asList("错误", "错", "否", "false", "×").contains(normalized)) { return "false"; }
            throw new IOException("无法识别判断题答案；测验未提交");
        }
        boolean multiple = "multiple".equals(question.getType());
        if (!multiple && !"single".equals(question.getType())) {
            throw new IOException("暂不支持该测验题型：" + question.getType() + "；测验未提交");
        }
        SortedSet<String> letters = new TreeSet<>();
        String[] answers = multiple ? answer.split("#", -1) : new String[] { answer };
        for (String part : answers) {
            String expected = comparableOption(part);
            Set<String> matches = new HashSet<>();
            if (question.getTKOptions() != null && !expected.isEmpty()) {
                for (String option : question.getTKOptions()) {
                    String label = text(option);
                    if (!label.matches("^[A-Z](?:[.、:：)）\\s].*|[^A-Za-z].*)$")) { continue; }
                    String content = label.substring(1).replaceFirst("^[.、:：)）\\s]+", "");
                    if (comparableOption(content).equals(expected)) { matches.add(label.substring(0, 1)); }
                }
            }
            if (matches.size() != 1) { throw new IOException("题库答案无法唯一匹配当前选项；测验未提交"); }
            letters.add(matches.iterator().next());
        }
        if (!multiple && letters.size() != 1) { throw new IOException("单选题答案数量不正确；测验未提交"); }
        return String.join("", letters);
    }
}
