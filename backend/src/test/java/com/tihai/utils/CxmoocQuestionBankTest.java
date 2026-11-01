package com.tihai.utils;

import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.junit.jupiter.api.Test;
import org.springframework.http.*;
import org.springframework.mock.http.client.reactive.MockClientHttpRequest;
import org.springframework.web.reactive.function.client.*;
import reactor.core.publisher.Mono;
import java.io.IOException;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.jupiter.api.Assertions.*;

class CxmoocQuestionBankTest {
    private Question single() {
        Question q = new Question(); q.setTitle("1 【单选题】 中国的首都是哪里？"); q.setType("single");
        q.setTKOptions(Arrays.asList("A 上海", "B. 广州", "C、北京", "D 深圳")); return q;
    }

    private String singleResponse() {
        return "[{\"index\":0,\"result\":[{\"topic\":\"中国的首都是哪里？\",\"type\":1,"
                + "\"correct\":[{\"option\":\"A\",\"content\":\"<b>北京</b>\"}]}]}]";
    }

    @Test
    void usesCurrentOptionTextInsteadOfStoredLetter() throws Exception {
        String answer = CxmoocQuestionBank.parseAnswer(singleResponse(), single());
        assertEquals("北京", answer); assertEquals("C", QuizAnswers.resolve(single(), answer));
        assertEquals("3.14是圆周率的近似值", QuizAnswers.title("3.14是圆周率的近似值"));
    }

    @Test
    void emptyWrongIndexWrongTypeAndDifferentQuestionAreRejected() {
        for (String raw : Arrays.asList("[]", "<html>error</html>", "{}",
                singleResponse().replace("\"index\":0", "\"index\":1"),
                singleResponse().replace("\"type\":1", "\"type\":2"),
                singleResponse().replace("中国的首都是哪里？", "中国的首都不是哪里？"))) {
            assertThrows(IOException.class, () -> CxmoocQuestionBank.parseAnswer(raw, single()), raw);
        }
    }

    @Test
    void flowControlAndConflictingAnswersStopQuery() {
        assertTrue(assertThrows(IOException.class, () -> CxmoocQuestionBank.parseAnswer(
                "{\"code\":-2,\"msg\":\"flow control\"}", single())).getMessage().contains("限流"));
        String twoRows = singleResponse().substring(0, singleResponse().length() - 1) + ","
                + singleResponse().substring(1).replace("北京", "上海");
        assertThrows(IOException.class, () -> CxmoocQuestionBank.parseAnswer(twoRows, single()));
    }

    @Test
    void multipleAnswersMustAllMatchAndSingleCannotUseSubstringOrAmbiguousMatch() throws Exception {
        Question q = new Question(); q.setType("multiple"); q.setTitle("哪些是水果？");
        q.setTKOptions(Arrays.asList("A 苹果", "B 香蕉", "C 土豆"));
        String raw = "[{\"index\":0,\"result\":[{\"topic\":\"哪些是水果？\",\"type\":2,"
                + "\"correct\":[{\"option\":\"X\",\"content\":\"香蕉\"},{\"option\":\"Y\",\"content\":\"苹果\"}]}]}]";
        assertEquals("AB", QuizAnswers.resolve(q, CxmoocQuestionBank.parseAnswer(raw, q)));
        assertThrows(IOException.class, () -> QuizAnswers.resolve(q, "苹果#未知答案"));
        assertThrows(IOException.class, () -> QuizAnswers.resolve(single(), "北"));
        Question ambiguous = single(); ambiguous.setTKOptions(Arrays.asList("A 北京", "B 北京"));
        assertThrows(IOException.class, () -> QuizAnswers.resolve(ambiguous, "北京"));
    }

    @Test
    void judgementSupportsBooleanContentButNeverGuessesUnknownValues() throws Exception {
        Question q = new Question(); q.setTitle("测试判断"); q.setType("judgement");
        String raw = "[{\"index\":0,\"result\":[{\"topic\":\"测试判断\",\"type\":3,\"correct\":[{\"content\":true}]}]}]";
        assertEquals("true", QuizAnswers.resolve(q, CxmoocQuestionBank.parseAnswer(raw, q)));
        assertEquals("false", QuizAnswers.resolve(q, "错"));
        assertThrows(IOException.class, () -> QuizAnswers.resolve(q, "不确定"));
    }

    @Test
    void sendsExpectedFormVersionAndNoAccountCookies() throws Exception {
        WebClient client = WebClient.builder().exchangeFunction(request -> {
            assertEquals("https://cx.icodef.com/v2/answer?platform=cx", request.url().toString());
            assertEquals("2.5", request.headers().getFirst("X-Version"));
            assertEquals("Mozilla/5.0", request.headers().getFirst("User-Agent"));
            assertFalse(request.headers().containsKey("Cookie"));
            assertFalse(request.headers().containsKey("Authorization"));
            assertTrue(request.headers().getContentType().isCompatibleWith(MediaType.APPLICATION_FORM_URLENCODED));
            MockClientHttpRequest capture = new MockClientHttpRequest(HttpMethod.POST, request.url());
            request.writeTo(capture, ExchangeStrategies.withDefaults()).block();
            String form = capture.getBodyAsString().block();
            assertTrue(form.contains("topic%5B0%5D=")); assertTrue(form.contains("type%5B0%5D=1"));
            assertTrue(form.contains("info=%7B%7D"));
            return Mono.just(ClientResponse.create(HttpStatus.OK).body(singleResponse()).build());
        }).build();
        assertEquals("北京", new CxmoocQuestionBank(client, new TiKuConfig()).queryAnswer(single()));
    }

    @Test
    void rechecksLetterOnlyResponseUntilItHasCurrentOptionText() throws Exception {
        AtomicInteger requests=new AtomicInteger();
        WebClient client=WebClient.builder().exchangeFunction(request->Mono.just(ClientResponse.create(HttpStatus.OK)
                .body(requests.incrementAndGet()==1?singleResponse().replace("<b>北京</b>","C"):singleResponse()).build())).build();
        assertEquals("北京",new CxmoocQuestionBank(client,new TiKuConfig()).queryAnswer(single()));
        assertEquals(2,requests.get());
    }

    @Test
    void repeatedLettersStopAfterThreeRequestsWithoutGuessingCurrentOrder() {
        AtomicInteger requests=new AtomicInteger();
        WebClient client=WebClient.builder().exchangeFunction(request->{requests.incrementAndGet();return Mono.just(ClientResponse.create(HttpStatus.OK)
                .body(singleResponse().replace("<b>北京</b>","C")).build());}).build();
        assertTrue(assertThrows(IOException.class,()->new CxmoocQuestionBank(client,new TiKuConfig()).queryAnswer(single())).getMessage().contains("3 次"));
        assertEquals(3,requests.get());
    }

    @Test
    void flowControlIsNotRetried() {
        AtomicInteger requests=new AtomicInteger();
        WebClient client=WebClient.builder().exchangeFunction(request->{requests.incrementAndGet();return Mono.just(ClientResponse.create(HttpStatus.OK)
                .body("{\"code\":-2}").build());}).build();
        assertTrue(assertThrows(IOException.class,()->new CxmoocQuestionBank(client,new TiKuConfig()).queryAnswer(single())).getMessage().contains("限流"));
        assertEquals(1,requests.get());
    }

    @Test
    void concurrentLocalRequestsAreSerializedAndSpaced() throws Exception {
        AtomicInteger active = new AtomicInteger(); AtomicInteger maximum = new AtomicInteger();
        List<Long> starts = Collections.synchronizedList(new ArrayList<>());
        WebClient client = WebClient.builder().exchangeFunction(request -> {
            starts.add(System.nanoTime()); maximum.accumulateAndGet(active.incrementAndGet(), Math::max);
            return Mono.delay(Duration.ofMillis(30)).map(ignored -> {
                active.decrementAndGet(); return ClientResponse.create(HttpStatus.OK).body(singleResponse()).build();
            });
        }).build();
        CxmoocQuestionBank bank = new CxmoocQuestionBank(client, new TiKuConfig());
        ExecutorService pool = Executors.newFixedThreadPool(2);
        try {
            Future<String> first = pool.submit(() -> bank.queryAnswer(single()));
            Future<String> second = pool.submit(() -> bank.queryAnswer(single()));
            assertEquals("北京", first.get(5, TimeUnit.SECONDS));
            assertEquals("北京", second.get(5, TimeUnit.SECONDS));
            assertEquals(1, maximum.get());
            assertTrue(TimeUnit.NANOSECONDS.toMillis(starts.get(1) - starts.get(0)) >= 1400);
        } finally { pool.shutdownNow(); }
    }
}
