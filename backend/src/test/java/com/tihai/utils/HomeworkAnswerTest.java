package com.tihai.utils;

import com.tihai.controller.LocalQuizController;
import org.junit.jupiter.api.Test;
import org.springframework.mock.http.server.reactive.MockServerHttpRequest;
import org.springframework.test.web.reactive.server.WebTestClient;
import java.net.InetSocketAddress;
import java.io.IOException;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.mockito.ArgumentMatchers.any;

class HomeworkAnswerTest {
    private MockServerHttpRequest local(){return MockServerHttpRequest.post("/").remoteAddress(new InetSocketAddress("127.0.0.1",1234)).build();}
    private LocalQuizController.HomeworkLookup question(String type){
        LocalQuizController.HomeworkLookup q=new LocalQuizController.HomeworkLookup();
        q.id="101";q.title="合成题目";q.type=type;q.options=Arrays.asList("A 上海","B 北京");return q;
    }
    @Test void matchesCurrentOptionsAndNeverInventsAnAnswer() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        when(bank.queryAnswerSync(any())).thenReturn("北京");
        Map<String,Object> answer=controller.homeworkAnswer(question("single"),local());
        assertEquals(true,answer.get("matched"));assertEquals("B",answer.get("selection"));assertFalse(answer.containsKey("submit"));
        for(String missing:Arrays.asList(null,"","不存在的选项")){
            when(bank.queryAnswerSync(any())).thenReturn(missing);
            answer=controller.homeworkAnswer(question("single"),local());
            assertEquals(false,answer.get("matched"));assertFalse(answer.containsKey("selection"));
        }
    }
    @Test void providerFailuresAndConflictingOptionsLeaveTheQuestionBlank() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        when(bank.queryAnswerSync(any())).thenThrow(new IOException("provider unavailable"));
        Map<String,Object> failed=controller.homeworkAnswer(question("single"),local());
        assertEquals(false,failed.get("matched"));assertEquals("provider-error",failed.get("reason"));
        reset(bank);when(bank.queryAnswerSync(any())).thenReturn("北京");
        LocalQuizController.HomeworkLookup q=question("single");q.options=Arrays.asList("A 北京","B 北京");
        Map<String,Object> uncertain=controller.homeworkAnswer(q,local());
        assertEquals(false,uncertain.get("matched"));assertEquals("option-mismatch",uncertain.get("reason"));
    }
    @Test void preservesBlankOrderAndRequiresTheExactNumberOfAnswers() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        LocalQuizController.HomeworkLookup q=question("fill");q.options=Collections.emptyList();q.blankCount=2;
        when(bank.queryAnswerSync(any())).thenReturn("乙#甲");
        assertEquals(Arrays.asList("乙","甲"),controller.homeworkAnswer(q,local()).get("values"));
        for(String bad:Arrays.asList("只有一个","甲#","甲#乙#丙")){
            when(bank.queryAnswerSync(any())).thenReturn(bad);
            assertEquals(false,controller.homeworkAnswer(q,local()).get("matched"));
        }
    }
    @Test void unrecognizedTypesNeverReachTheBankAndWebOriginsAreRejected() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        assertEquals(false,controller.homeworkAnswer(question("unsupported"),local()).get("matched"));verifyNoInteractions(bank);
        MockServerHttpRequest web=MockServerHttpRequest.post("/").remoteAddress(new InetSocketAddress("127.0.0.1",1234)).header("Origin","https://example.invalid").build();
        assertThrows(Exception.class,()->controller.homeworkAnswer(question("single"),web));
        MockServerHttpRequest remote=MockServerHttpRequest.post("/").remoteAddress(new InetSocketAddress("192.0.2.1",1234)).build();
        assertThrows(Exception.class,()->controller.homeworkAnswer(question("single"),remote));
    }
    @Test void cxmoocPreservesTheOrderOfMultipleBlanks() throws Exception {
        com.tihai.common.Question q=new com.tihai.common.Question();q.setType("fill");q.setTitle("合成填空");
        String raw="[{\"index\":0,\"result\":[{\"topic\":\"合成填空\",\"type\":4,\"correct\":[{\"content\":\"乙\"},{\"content\":\"甲\"}]}]}]";
        assertEquals("乙#甲",CxmoocQuestionBank.parseAnswer(raw,q));
    }
    @Test void reportsBlankReasonsWithoutReturningProviderDetails() throws Exception {
        Query bank=mock(Query.class);LocalQuizController controller=new LocalQuizController(bank);
        for(String[] row:new String[][]{{"cxmooc 未查到题干、题型一致的答案","no-match"},{"cxmooc 返回互相冲突的答案","answer-conflict"},{"cxmooc 题库连接失败或超时","provider-error"},{"cxmooc 暂不支持该题型","unsupported-type"}}){
            doThrow(new IOException(row[0])).when(bank).queryAnswerSync(any());
            Map<String,Object> answer=controller.homeworkAnswer(question("single"),local());
            assertEquals(false,answer.get("matched"));assertEquals(row[1],answer.get("reason"));assertFalse(answer.containsKey("selection"));assertFalse(answer.toString().contains(row[0]));
        }
        LocalQuizController.HomeworkLookup fill=question("fill");fill.blankCount=2;
        doReturn("只有一个空的答案").when(bank).queryAnswerSync(any());
        assertEquals("blank-count-mismatch",controller.homeworkAnswer(fill,local()).get("reason"));
    }
    @Test void desktopPayloadReachesTheHomeworkEndpointOnABlockingWorker() throws Exception {
        Query bank=mock(Query.class);when(bank.queryAnswerSync(any())).thenAnswer(invocation->{
            assertFalse(reactor.core.scheduler.Schedulers.isInNonBlockingThread());return "北京";
        });
        WebTestClient client=WebTestClient.bindToController(new LocalQuizController(bank))
                .webFilter((exchange,chain)->chain.filter(exchange.mutate().request(exchange.getRequest().mutate()
                        .remoteAddress(new InetSocketAddress("127.0.0.1",1234)).build()).build())).build();
        Map<String,Object> payload=new HashMap<>();payload.put("id","101");payload.put("title","合成题目");payload.put("type","single");
        payload.put("options",Arrays.asList("A 上海","B 北京"));payload.put("blankCount",0);payload.put("answered",false);
        client.post().uri("/local/desktop/quiz/homework-answer").bodyValue(payload).exchange().expectStatus().isOk()
                .expectBody().jsonPath("$.matched").isEqualTo(true).jsonPath("$.selection").isEqualTo("B");
    }
}
