package com.tihai.utils;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.*;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpStatus;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.reactive.function.client.*;
import reactor.core.publisher.Mono;
import java.io.IOException;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.mockito.ArgumentMatchers.any;

class BuguakeQuestionBankTest {
    private static final ObjectMapper JSON = new ObjectMapper();
    private Question single() {
        Question q = new Question();q.setTitle("目标问题？");q.setType("single");
        q.setTKOptions(Arrays.asList("A 上海", "B 北京", "C 深圳"));return q;
    }
    private ObjectNode item(String title, String type, String[] options, int... yes) {
        ObjectNode item=JSON.createObjectNode();item.putArray("que_stem").addObject().put("c",title);
        item.putObject("que_info").put("type",type);
        ArrayNode group=item.putArray("que_options").addArray();
        for(int i=0;i<options.length;i++) {
            ObjectNode option=group.addObject();boolean correct=false;for(int index:yes)correct|=index==i;
            option.put("yes",correct);option.putArray("ret").addObject().put("c",options[i]);
        }
        return item;
    }
    private ObjectNode row(ObjectNode item) throws Exception {
        String salt="fixture",key="34cab29ef956d78afd"+salt;
        String encoded=URLEncoder.encode(JSON.writeValueAsString(item),"UTF-8");
        byte[] bytes=new byte[encoded.length()*2];
        for(int i=0;i<encoded.length();i++)bytes[i*2]=(byte)(encoded.charAt(i)^key.charAt((i*2)%key.length())^key.charAt((i*2+1)%key.length()));
        return JSON.createObjectNode().put("actk",salt).put("bdjson",Base64.getEncoder().encodeToString(bytes));
    }
    private String response(ObjectNode... items) throws Exception {
        ObjectNode root=JSON.createObjectNode().put("errno",0);ArrayNode list=root.putObject("data").putArray("list");
        for(ObjectNode item:items)list.add(row(item));return JSON.writeValueAsString(root);
    }
    private ObjectNode valid() {return item("【单选题】目标问题？","sc",new String[]{"北京","深圳","上海"},0);}
    private String parse(ObjectNode... items) throws Exception {return BuguakeQuestionBank.parseAnswer(response(items),single());}

    @Test void capturedPublicResponseDecodesAndRemapsActualOptions() throws Exception {
        JsonNode captured=JSON.readTree(getClass().getResourceAsStream("/buguake-capital.json"));
        Question q=single();q.setTitle("我国的首都是哪里？");q.setTKOptions(Arrays.asList("A 北京","B 深圳","C 上海","D 广州"));
        assertEquals("A",QuizAnswers.resolve(q,BuguakeQuestionBank.parseAnswer(captured.toString(),q)));
    }
    @Test void shuffledOptionsUseTextAndIgnoreDuplicatesInSearch() throws Exception {
        assertEquals("B",QuizAnswers.resolve(single(),parse(valid(),valid())));
    }
    @Test void wrongStemNegationTypeAndOptionSetAreRejected() throws Exception {
        for(ObjectNode item:Arrays.asList(item("目标问题不正确？","sc",new String[]{"北京","深圳","上海"},0),
                item("目标问题？","mc",new String[]{"北京","深圳","上海"},0),
                item("目标问题？","sc",new String[]{"北京","广州","上海"},0),
                item("目标问题？","sc",new String[]{"北京","深圳","上海"},0,1))) {
            assertThrows(BuguakeQuestionBank.NoMatchException.class,()->parse(item));
        }
    }
    @Test void imagesAndDuplicateOptionTextAreRejected() throws Exception {
        ObjectNode image=valid();((ObjectNode)image.path("que_info")).putObject("meta").put("img_que_options",true);
        assertThrows(BuguakeQuestionBank.NoMatchException.class,()->parse(image));
        assertThrows(BuguakeQuestionBank.NoMatchException.class,()->parse(item("目标问题？","sc",new String[]{"北京","北京","上海"},0)));
    }
    @Test void conflictNeverUsesTheFirstResult() throws Exception {
        assertTrue(assertThrows(IOException.class,()->parse(valid(),item("目标问题？","sc",new String[]{"北京","深圳","上海"},1))).getMessage().contains("冲突"));
    }
    @Test void multipleMatchesCompleteSetAndCurrentOrder() throws Exception {
        Question q=single();q.setType("multiple");
        ObjectNode source=item("目标问题？","mc",new String[]{"北京","深圳","上海"},0,1);
        assertEquals("BC",QuizAnswers.resolve(q,BuguakeQuestionBank.parseAnswer(response(source),q)));
    }
    @Test void judgementOnlyAcceptsSameStatementWithExplicitVerdict() throws Exception {
        Question q=single();q.setType("judgement");
        ObjectNode source=item("目标问题？","tf",new String[0]);source.putArray("que_answer").addObject().put("c","错误");
        assertEquals("false",QuizAnswers.resolve(q,BuguakeQuestionBank.parseAnswer(response(source),q)));
        source.putArray("que_answer").addObject().put("c","不确定");
        assertThrows(BuguakeQuestionBank.NoMatchException.class,()->BuguakeQuestionBank.parseAnswer(response(source),q));
    }
    @Test void emptySearchDiffersFromProtocolFailure() throws Exception {
        assertThrows(BuguakeQuestionBank.NoMatchException.class,()->BuguakeQuestionBank.parseAnswer(response(),single()));
        assertThrows(BuguakeQuestionBank.NoMatchException.class,()->BuguakeQuestionBank.parseAnswer("{\"errno\":0,\"errmsg\":\"success\",\"data\":[]}",single()));
        for(String raw:Arrays.asList("<html>login</html>","{}","{\"errno\":1,\"data\":{\"list\":[]}}","{\"errno\":0,\"data\":{\"list\":[{}]}}")) {
            IOException error=assertThrows(IOException.class,()->BuguakeQuestionBank.parseAnswer(raw,single()));
            assertFalse(error instanceof BuguakeQuestionBank.NoMatchException);
        }
    }
    @Test void querySendsOnlyPublicStemAndNoCredentials() throws Exception {
        WebClient client=WebClient.builder().defaultHeader("Cookie","PRIVATE").defaultHeader("Authorization","PRIVATE")
                .exchangeFunction(request->{
                    assertEquals("GET",request.method().name());assertEquals("easylearn.baidu.com",request.url().getHost());
                    assertTrue(request.url().getQuery().contains("rn=10"));assertFalse(request.url().getQuery().contains("PRIVATE"));
                    assertFalse(request.headers().containsKey("Cookie"));assertFalse(request.headers().containsKey("Authorization"));
                    try{return Mono.just(ClientResponse.create(HttpStatus.OK).body(response(valid())).build());}
                    catch(Exception e){return Mono.error(e);}
                }).build();
        assertEquals("北京",new BuguakeQuestionBank(client,new TiKuConfig()).queryAnswer(single()));
    }
    private Query chain(CxmoocQuestionBank primary,BuguakeQuestionBank supplement,boolean enabled) {
        Query query=new Query();TiKuConfig config=new TiKuConfig();config.setProvider("cxmooc");config.setBuguakeEnabled(enabled);
        ReflectionTestUtils.setField(query,"tiKuConfig",config);ReflectionTestUtils.setField(query,"cxmooc",primary);ReflectionTestUtils.setField(query,"buguake",supplement);return query;
    }
    @Test void primaryMissFallsBackBeforeReturningAnyAnswer() throws Exception {
        CxmoocQuestionBank primary=mock(CxmoocQuestionBank.class);BuguakeQuestionBank supplement=mock(BuguakeQuestionBank.class);
        when(primary.queryAnswer(any())).thenThrow(new IOException("cxmooc 未查到题干、题型一致的答案；测验未提交"));
        when(supplement.queryAnswer(any())).thenReturn("北京");
        assertEquals("B",QuizAnswers.resolve(single(),chain(primary,supplement,true).queryAnswerSync(single())));verify(supplement).queryAnswer(any());
    }
    @Test void invalidPrimaryAnswerCanUseSupplementButValidAnswerStopsSearching() throws Exception {
        CxmoocQuestionBank primary=mock(CxmoocQuestionBank.class);BuguakeQuestionBank supplement=mock(BuguakeQuestionBank.class);
        when(primary.queryAnswer(any())).thenReturn("B");when(supplement.queryAnswer(any())).thenReturn("北京");
        assertEquals("北京",chain(primary,supplement,true).queryAnswerSync(single()));verify(supplement).queryAnswer(any());
        reset(supplement);when(primary.queryAnswer(any())).thenReturn("北京");
        assertEquals("北京",chain(primary,supplement,true).queryAnswerSync(single()));verifyNoInteractions(supplement);
    }
    @Test void disabledSupplementIsNeverCalled() throws Exception {
        CxmoocQuestionBank primary=mock(CxmoocQuestionBank.class);BuguakeQuestionBank supplement=mock(BuguakeQuestionBank.class);
        when(primary.queryAnswer(any())).thenThrow(new IOException("primary miss"));
        assertThrows(IOException.class,()->chain(primary,supplement,false).queryAnswerSync(single()));verifyNoInteractions(supplement);
    }
    @Test void secondaryFailureCannotBecomeRandomAnswerTrigger() throws Exception {
        CxmoocQuestionBank primary=mock(CxmoocQuestionBank.class);BuguakeQuestionBank supplement=mock(BuguakeQuestionBank.class);
        IOException missing=new IOException("cxmooc 未查到题干、题型一致的答案；测验未提交");when(primary.queryAnswer(any())).thenThrow(missing);
        when(supplement.queryAnswer(any())).thenThrow(new IOException("不挂科连接失败"));
        assertEquals("不挂科连接失败",assertThrows(IOException.class,()->chain(primary,supplement,true).queryAnswerSync(single())).getMessage());
        doThrow(new BuguakeQuestionBank.NoMatchException()).when(supplement).queryAnswer(any());
        assertSame(missing,assertThrows(IOException.class,()->chain(primary,supplement,true).queryAnswerSync(single())));
    }
    @Test void primaryNetworkErrorRemainsAnErrorIfSupplementMisses() throws Exception {
        CxmoocQuestionBank primary=mock(CxmoocQuestionBank.class);BuguakeQuestionBank supplement=mock(BuguakeQuestionBank.class);
        IOException network=new IOException("cxmooc 题库连接失败或超时；测验未提交");when(primary.queryAnswer(any())).thenThrow(network);
        when(supplement.queryAnswer(any())).thenThrow(new BuguakeQuestionBank.NoMatchException());
        assertSame(network,assertThrows(IOException.class,()->chain(primary,supplement,true).queryAnswerSync(single())));
    }
}
