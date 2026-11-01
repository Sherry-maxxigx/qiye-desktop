package com.tihai.utils;

import com.tihai.controller.LocalQuizController;
import org.junit.jupiter.api.Test;
import org.springframework.mock.http.server.reactive.MockServerHttpRequest;
import org.springframework.test.web.reactive.server.WebTestClient;
import java.net.InetSocketAddress;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.mockito.ArgumentMatchers.any;

class LocalQuizControllerTest {
    private MockServerHttpRequest local() {return MockServerHttpRequest.post("/").remoteAddress(new InetSocketAddress("127.0.0.1",1234)).build();}
    @Test void plaintextQuestionsDecodeWithoutSecretFontAndExcludeHiddenTokens() throws Exception {
        String html="<form><input name='enc' value='DO_NOT_EXPORT'><div class='singleQuesId' data='123'><div class='TiMu' data='0'>"
                +"<div class='Zy_TItle'>【单选题】城市？</div><ul><li>A 北京</li><li>B 上海</li></ul></div></div></form>";
        Map<String,Object> parsed=new LocalQuizController(mock(Query.class)).questions(Collections.singletonMap("html",html),local());
        assertEquals(1,parsed.get("total"));assertFalse(parsed.toString().contains("DO_NOT_EXPORT"));
        assertTrue(parsed.toString().contains("北京"));
    }
    @Test void answersUseCurrentOptionsRatherThanBankLetters() throws Exception {
        Query bank=mock(Query.class);when(bank.queryAnswerSync(any())).thenReturn("北京");
        LocalQuizController.Lookup q=new LocalQuizController.Lookup();q.id="123";q.title="城市？";q.type="single";q.options=Arrays.asList("A 上海","B 北京");
        assertEquals("B",new LocalQuizController(bank).answer(q,local()).get("selection"));
    }
    @Test void missingAnswersAndUnknownTypesStopBeforeAnySubmission() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        LocalQuizController.Lookup q=new LocalQuizController.Lookup();q.id="123";q.title="城市？";q.type="single";q.options=Arrays.asList("A 上海","B 北京");
        assertThrows(Exception.class,()->controller.answer(q,local()));
        q.type="short";assertThrows(Exception.class,()->controller.answer(q,local()));
    }
    @Test void browserOriginsCannotCallLocalQuizEndpoints() {
        MockServerHttpRequest request=MockServerHttpRequest.post("/").remoteAddress(new InetSocketAddress("127.0.0.1",1234)).header("Origin","https://unrelated.invalid").build();
        assertThrows(Exception.class,()->new LocalQuizController(mock(Query.class)).questions(Collections.singletonMap("html","<form/>"),request));
    }
    @Test void reactiveHttpEndpointInjectsRequestAndReturnsMappedAnswer() throws Exception {
        Query bank=mock(Query.class);when(bank.queryAnswerSync(any())).thenAnswer(invocation->{
            assertFalse(reactor.core.scheduler.Schedulers.isInNonBlockingThread(),"Bank lookup must run on a worker that allows blocking");
            return "北京";
        });
        WebTestClient client=WebTestClient.bindToController(new LocalQuizController(bank))
                .webFilter((exchange,chain)->chain.filter(exchange.mutate().request(exchange.getRequest().mutate()
                        .remoteAddress(new InetSocketAddress("127.0.0.1",1234)).build()).build())).build();
        Map<String,Object> q=new HashMap<>();q.put("id","123");q.put("title","城市？");q.put("type","single");q.put("options",Arrays.asList("A 上海","B 北京"));
        client.post().uri("/local/desktop/quiz/answer").bodyValue(q).exchange().expectStatus().isOk().expectBody().jsonPath("$.selection").isEqualTo("B");
    }
}
