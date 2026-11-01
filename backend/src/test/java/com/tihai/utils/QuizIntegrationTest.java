package com.tihai.utils;

import com.tihai.common.Question;
import com.tihai.domain.chaoxing.SuperStarLog;
import okhttp3.OkHttpClient;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpStatus;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.reactive.function.client.ClientResponse;
import org.springframework.web.reactive.function.client.WebClient;
import reactor.core.publisher.Mono;

import java.io.IOException;
import java.util.*;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

class QuizIntegrationTest {
    @Test
    void oldProviderRedirectAndGatewayFailureAreExplicit() {
        IOException redirect = assertThrows(IOException.class, () -> queryWith(301, "text/html", "<html>301</html>"));
        assertTrue(redirect.getMessage().contains("301"));
        IOException gateway = assertThrows(IOException.class, () -> queryWith(502, "text/html", "<html>502</html>"));
        assertTrue(gateway.getMessage().contains("502"));
    }

    @Test
    void htmlAndMalformedJsonAreRejected() {
        assertTrue(assertThrows(IOException.class, () -> queryWith(200, "text/html", "<html>error</html>"))
                .getMessage().contains("网页"));
        assertThrows(IOException.class, () -> Query.parseAnswer("not json"));
    }

    @Test
    void successfulAnswerAllowsAdditionalProviderFields() throws Exception {
        assertEquals("答案", Query.parseAnswer("{\"code\":200,\"data\":\" 答案 \",\"extra\":true}"));
        assertEquals("答案", queryWith(200, "application/json", "{\"code\":200,\"data\":\"答案\"}"));
    }

    @Test
    void missingAnswerAndIncompatibleSchemasStopInsteadOfBecomingAnswers() {
        for (String raw : Arrays.asList("", "null", "{}", "{\"code\":500,\"data\":\"error\"}",
                "{\"code\":200,\"data\":null}", "{\"code\":200,\"data\":\" \"}",
                "{\"code\":200,\"data\":{\"answer\":\"A\"}}")) {
            assertThrows(IOException.class, () -> Query.parseAnswer(raw), raw);
        }
    }

    @Test
    void submissionAcknowledgementAcceptsBooleanAndStringButRejectsFailure() throws Exception {
        Map<String, Object> result = new HashMap<>();
        result.put("msg", "success"); result.put("status", true);
        CourseUtil.requireQuizSubmissionSuccess(result);
        result.put("status", "true"); CourseUtil.requireQuizSubmissionSuccess(result);
        result.put("status", false);
        assertThrows(IOException.class, () -> CourseUtil.requireQuizSubmissionSuccess(result));
        assertThrows(IOException.class, () -> CourseUtil.requireQuizSubmissionSuccess(Collections.emptyMap()));
    }

    @Test
    void unavailableAnswersNeverReachSubmission() throws Exception {
        Query provider = mock(Query.class);
        when(provider.queryAnswerSync(any())).thenReturn(null);
        CourseUtil util = new CourseUtil();
        ReflectionTestUtils.setField(util, "query", provider);
        OkHttpClient client = mock(OkHttpClient.class);
        Map<String, Object> question = new HashMap<>();
        question.put("id", "1"); question.put("title", "diagnostic");
        question.put("options", "A. one\nB. two"); question.put("type", "single");
        question.put("answerField", new HashMap<String, Object>());
        Map<String, Object> form = new HashMap<>();
        form.put("questions", Collections.singletonList(question));
        assertThrows(IOException.class, () -> util.processAnswers(form, client, new SuperStarLog(), "diagnostic"));
        verifyNoInteractions(client);
    }

    @Test
    void emptyQuizPageNeverReachesSubmission() {
        OkHttpClient client = mock(OkHttpClient.class);
        assertThrows(IOException.class, () -> new CourseUtil().processAnswers(
                new HashMap<>(), client, new SuperStarLog(), "diagnostic"));
        verifyNoInteractions(client);
    }

    private String queryWith(int status, String contentType, String body) throws IOException {
        Query query = new Query();
        WebClient client = WebClient.builder().baseUrl("http://diagnostic.invalid")
                .exchangeFunction(request -> Mono.just(ClientResponse.create(HttpStatus.valueOf(status))
                        .header("Content-Type", contentType).body(body).build())).build();
        ReflectionTestUtils.setField(query, "tikuWebClient", client);
        Question question = new Question();
        question.setTitle("diagnostic"); question.setType("single");
        return query.queryAnswerSync(question);
    }
}
