package com.tihai.utils;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.http.HttpStatus;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.reactive.function.client.ClientResponse;
import org.springframework.web.reactive.function.client.WebClient;
import reactor.core.publisher.Mono;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

/** Synthetic data only; no external course question data is redistributed. */
class LocalQuestionBankTest {
    @TempDir Path directory;
    private final TiKuConfig config = new TiKuConfig();
    private final LocalQuestionBank bank = new LocalQuestionBank(config);
    private final AtomicInteger onlineCalls = new AtomicInteger();

    private Map<String, Object> row(String title, String type, String[] options, String... answers) {
        Map<String, Object> row = new LinkedHashMap<>();
        row.put("question", title);
        row.put("type", type);
        List<Map<String, String>> choices = new ArrayList<>();
        for (int i = 0; i < options.length; i++) {
            Map<String, String> choice = new LinkedHashMap<>();
            choice.put("key", String.valueOf((char) ('A' + i)));
            choice.put("content", options[i]);
            choices.add(choice);
        }
        row.put("options", choices);
        row.put("correct_answers", Arrays.asList(answers));
        return row;
    }

    @SafeVarargs
    private final Path write(Map<String, Object>... rows) throws IOException {
        Path file = directory.resolve("bank.json");
        new ObjectMapper().writeValue(file.toFile(), Collections.singletonMap("questions", rows));
        config.setLocalBankFile(file.toString());
        return file;
    }

    private Question question(String title, String type, String... choices) {
        Question question = new Question();
        question.setTitle(title);
        question.setType(type);
        question.setTKOptions(Arrays.asList(choices));
        return question;
    }

    private Question single() {
        return question("Select blue", "single", "A blue", "B red");
    }

    private Query query() {
        Query query = new Query();
        ReflectionTestUtils.setField(query, "localBank", bank);
        ReflectionTestUtils.setField(query, "tiKuConfig", config);
        WebClient client = WebClient.builder().baseUrl("http://synthetic.invalid")
                .exchangeFunction(request -> {
                    onlineCalls.incrementAndGet();
                    return Mono.just(ClientResponse.create(HttpStatus.OK)
                            .header("Content-Type", "application/json")
                            .body("{\"code\":200,\"data\":\"blue\"}").build());
                }).build();
        ReflectionTestUtils.setField(query, "tikuWebClient", client);
        return query;
    }

    @Test
    void optionOrderUsesTextInsteadOfStoredLetters() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        Question reordered = question("Select blue", "single", "A red", "B blue");
        assertEquals("blue", bank.queryAnswer(reordered));
        assertEquals("B", QuizAnswers.resolve(reordered, bank.queryAnswer(reordered)));
    }

    @Test
    void titleFormattingAllowsSpacesFullWidthCharactersAndEmptyParentheses() throws Exception {
        write(row("选择蓝色 （）。", "single", new String[]{"蓝色", "红色"}, "A"));
        Question current = question("1【单选题】选择 蓝色：（）", "single", "A. 红色", "B. 蓝色");
        assertEquals("B", QuizAnswers.resolve(current, bank.queryAnswer(current)));
    }

    @Test
    void titleNormalizationRetainsNegationAndArithmeticSigns() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"),
                row("1 + 1 = ?", "single", new String[]{"two", "zero"}, "A"));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Do not select blue", "single", "A blue", "B red")));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("1 - 1 = ?", "single", "A two", "B zero")));
    }

    @Test
    void typeMustMatchEvenWhenTheAnswerWouldFit() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Select blue", "multiple", "A blue", "B red")));
    }

    @Test
    void fullOptionSetMustMatchIncludingDistractors() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Select blue", "single", "A blue", "B green")));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Select blue", "single", "A blue", "B red", "C green")));
    }

    @Test
    void duplicateCurrentTextOrLabelsCannotBecomeAnAnswer() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Select blue", "single", "A blue", "B blue")));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Select blue", "single", "A blue", "A red")));
    }

    @Test
    void multipleAnswersRemapToTheCurrentOrder() throws Exception {
        write(row("Select cool colors", "multiple", new String[]{"blue", "red", "green"}, "A", "C"));
        Question current = question("Select cool colors", "multiple", "A green", "B blue", "C red");
        assertEquals("AB", QuizAnswers.resolve(current, bank.queryAnswer(current)));
    }

    @Test
    void judgementAcceptsOnlyKnownBooleanAnswers() throws Exception {
        write(row("Synthetic true statement", "judge", new String[0], "正确"),
                row("Synthetic false statement", "judge", new String[0], "错误"),
                row("Uncertain statement", "judge", new String[0], "maybe"));
        assertEquals("true", bank.queryAnswer(question("Synthetic true statement", "judgement")));
        assertEquals("false", bank.queryAnswer(question("Synthetic false statement", "judgement")));
        assertThrows(LocalQuestionBank.NoMatchException.class,
                () -> bank.queryAnswer(question("Uncertain statement", "judgement")));
    }

    @Test
    void invalidRowsAndPlaceholdersAreSkippedWithoutBlockingValidRows() throws Exception {
        write(row("[空题]", "single", new String[]{"blue", "red"}, "A"),
                row("Duplicate options", "single", new String[]{"blue", "blue"}, "A"),
                row("Missing option", "single", new String[]{"blue", "red"}, "C"),
                row("Too many answers", "single", new String[]{"blue", "red"}, "A", "B"),
                row("Unknown type", "short", new String[]{"blue", "red"}, "A"),
                row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        for (String title : Arrays.asList("[空题]", "Duplicate options", "Missing option", "Too many answers", "Unknown type")) {
            assertThrows(LocalQuestionBank.NoMatchException.class,
                    () -> bank.queryAnswer(question(title, "single", "A blue", "B red")));
        }
        assertEquals("blue", bank.queryAnswer(single()));
    }

    @Test
    void conflictingAnswersWithTheSameStemAndOptionsStopLookup() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"),
                row("Select blue", "single", new String[]{"blue", "red"}, "B"));
        assertThrows(LocalQuestionBank.ConflictException.class, () -> bank.queryAnswer(single()));
    }

    @Test
    void identicalDuplicateAnswersRemainUsableAfterOptionReordering() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"),
                row("Select blue", "single", new String[]{"red", "blue"}, "B"));
        assertEquals("blue", bank.queryAnswer(single()));
    }

    @Test
    void judgementConflictDoesNotPickTheFirstCandidate() throws Exception {
        write(row("Synthetic statement", "judge", new String[0], "正确"),
                row("Synthetic statement", "judge", new String[0], "错误"));
        assertThrows(LocalQuestionBank.ConflictException.class,
                () -> bank.queryAnswer(question("Synthetic statement", "judgement")));
    }

    @Test
    void fileChangesInvalidateTheCachedIndex() throws Exception {
        Path file = write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertEquals("blue", bank.queryAnswer(single()));
        FileTime originalTime = Files.getLastModifiedTime(file);
        write(row("Select blue", "single", new String[]{"blue", "red"}, "B"));
        Files.setLastModifiedTime(file, FileTime.fromMillis(originalTime.toMillis() + 2000));
        assertEquals("red", bank.queryAnswer(single()));
    }

    @Test
    void changingOrRemovingTheConfiguredFileCannotUseOldAnswers() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertEquals("blue", bank.queryAnswer(single()));
        config.setLocalBankFile(directory.resolve("missing.json").toString());
        assertThrows(IOException.class, () -> bank.queryAnswer(single()));
        config.setLocalBankFile("");
        assertThrows(LocalQuestionBank.NoMatchException.class, () -> bank.queryAnswer(single()));
    }

    @Test
    void malformedFilesAreRejectedAndRootArraysAreAccepted() throws Exception {
        Path file = write();
        Files.write(file, "not json".getBytes(StandardCharsets.UTF_8));
        assertThrows(IOException.class, () -> bank.queryAnswer(single()));
        new ObjectMapper().writeValue(file.toFile(), Collections.singletonList(
                row("Select blue", "single", new String[]{"blue", "red"}, "A")));
        assertEquals("blue", bank.queryAnswer(single()));
    }

    @Test
    void configuredLocalHitDoesNotCallAnOnlineProvider() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"));
        assertEquals("blue", query().queryAnswerSync(single()));
        assertEquals(0, onlineCalls.get());
    }

    @Test
    void missingLocalQuestionFallsBackToTheExistingProvider() throws Exception {
        write(row("Different stem", "single", new String[]{"blue", "red"}, "A"));
        assertEquals("blue", query().queryAnswerSync(single()));
        assertEquals(1, onlineCalls.get());
    }

    @Test
    void unreadableLocalFileFallsBackToTheExistingProvider() throws Exception {
        config.setLocalBankFile(directory.resolve("missing.json").toString());
        assertEquals("blue", query().queryAnswerSync(single()));
        assertEquals(1, onlineCalls.get());
    }

    @Test
    void localAnswerConflictDoesNotFallBackToAnotherAnswer() throws Exception {
        write(row("Select blue", "single", new String[]{"blue", "red"}, "A"),
                row("Select blue", "single", new String[]{"blue", "red"}, "B"));
        assertThrows(LocalQuestionBank.ConflictException.class, () -> query().queryAnswerSync(single()));
        assertEquals(0, onlineCalls.get());
    }

    @Test
    void emptyDefaultSettingKeepsTheExistingProvider() throws Exception {
        assertEquals("", config.getLocalBankFile());
        assertEquals("blue", query().queryAnswerSync(single()));
        assertEquals(1, onlineCalls.get());
    }
}
