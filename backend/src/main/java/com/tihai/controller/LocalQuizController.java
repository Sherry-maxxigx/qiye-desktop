package com.tihai.controller;

import com.tihai.common.Question;
import com.tihai.utils.CourseUtil;
import com.tihai.utils.Decode;
import com.tihai.utils.Query;
import com.tihai.utils.QuizAnswers;
import org.springframework.context.annotation.Profile;
import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.server.ResponseStatusException;
import org.springframework.http.server.reactive.ServerHttpRequest;
import reactor.core.publisher.Mono;
import reactor.core.scheduler.Schedulers;
import java.util.*;
import java.io.IOException;
import org.jsoup.Jsoup;

/** Decode and look up questions for the embedded official page; never submits platform forms. */
@Profile("local")
@RestController
@RequestMapping("/local/desktop/quiz")
public class LocalQuizController {
    private final Query bank;
    public LocalQuizController(Query bank) { this.bank=bank; }

    private static void local(ServerHttpRequest request) throws Exception {
        if (request.getRemoteAddress() == null || !request.getRemoteAddress().getAddress().isLoopbackAddress() || request.getHeaders().getOrigin() != null) {
            throw new ResponseStatusException(HttpStatus.FORBIDDEN,"仅供本机桌面程序调用");
        }
    }

    @PostMapping(value="/questions",consumes="application/json")
    public Mono<Map<String,Object>> questionRequest(@RequestBody Map<String,String> body,ServerHttpRequest request) {
        return Mono.fromCallable(()->questions(body,request)).subscribeOn(Schedulers.boundedElastic());
    }
    public Map<String,Object> questions(@RequestBody Map<String,String> body,ServerHttpRequest request) throws Exception {
        local(request);
        String html=body.get("html");
        if (html == null || html.length() > 2500000) throw new ResponseStatusException(HttpStatus.BAD_REQUEST,"测验页面无效或过大");
        List<Question> decoded=CourseUtil.workQuestions(Decode.decodeQuestionsInfo(html));
        if (decoded.size() > 100) throw new ResponseStatusException(HttpStatus.BAD_REQUEST,"测验题量超出桌面处理范围");
        List<Map<String,Object>> rows=new ArrayList<>();
        for (Question q:decoded) {
            Map<String,Object> row=new LinkedHashMap<>();
            row.put("id",q.getId());row.put("title",q.getTitle());row.put("type",q.getType());row.put("options",q.getTKOptions());
            rows.add(row);
        }
        Map<String,Object> result=new LinkedHashMap<>();result.put("total",rows.size());result.put("questions",rows);return result;
    }

    public static class Lookup {
        public String id,title,type;
        public List<String> options;
    }

    @PostMapping(value="/answer",consumes="application/json")
    public Mono<Map<String,String>> answerRequest(@RequestBody Lookup body,ServerHttpRequest request) {
        return Mono.fromCallable(()->answer(body,request)).subscribeOn(Schedulers.boundedElastic());
    }
    public Map<String,String> answer(@RequestBody Lookup body,ServerHttpRequest request) throws Exception {
        local(request);
        if (body.id == null || !body.id.matches("\\d{1,24}") || body.title == null || body.title.length()>4000
                || body.options == null || body.options.size()<2 || body.options.size()>26
                || !Arrays.asList("single","multiple","judgement").contains(body.type)) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST,"当前题目或题型暂不支持自动提交");
        }
        for(String option:body.options) if(option==null || option.length()>4000) throw new ResponseStatusException(HttpStatus.BAD_REQUEST,"选项无效");
        Question q=new Question();q.setId(body.id);q.setTitle(body.title);q.setType(body.type);q.setTKOptions(body.options);
        String answer=bank.queryAnswerSync(q);
        Map<String,String> result=new LinkedHashMap<>();result.put("id",body.id);result.put("selection",QuizAnswers.resolve(q,answer));return result;
    }

    public static class HomeworkLookup extends Lookup { public int blankCount; }

    /** Homework has an explicit no-guessing contract, separate from chapter quiz handling. */
    @PostMapping(value="/homework-answer",consumes="application/json")
    public Mono<Map<String,Object>> homeworkRequest(@RequestBody HomeworkLookup body,ServerHttpRequest request) {
        return Mono.fromCallable(()->homeworkAnswer(body,request)).subscribeOn(Schedulers.boundedElastic());
    }
    public Map<String,Object> homeworkAnswer(HomeworkLookup body,ServerHttpRequest request) throws Exception {
        local(request);
        if(body.id==null || !body.id.matches("\\d{1,24}") || body.title==null || body.title.trim().isEmpty() || body.title.length()>4000) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST,"作业题目无效");
        }
        Map<String,Object> result=new LinkedHashMap<>();result.put("id",body.id);result.put("matched",false);
        boolean choice=Arrays.asList("single","multiple","judgement").contains(body.type);
        boolean written=Arrays.asList("fill","short").contains(body.type);
        if(!choice && !written) {result.put("reason","unsupported-type");return result;}
        if(body.options==null)body.options=Collections.emptyList();
        if(body.options.size()>26 || choice && body.options.size()<2 || written && (body.blankCount<1 || body.blankCount>20)) {result.put("reason","unsupported-fields");return result;}
        for(String option:body.options)if(option==null || option.length()>4000){result.put("reason","unsupported-fields");return result;}
        Question question=new Question();question.setId(body.id);question.setTitle(body.title);question.setType(body.type);question.setTKOptions(body.options);
        try {
            String answer=bank.queryAnswerSync(question);
            if(choice)result.put("selection",QuizAnswers.resolve(question,answer));
            else {
                if(answer==null || answer.trim().isEmpty()){result.put("reason","no-match");return result;}
                String[] parts=answer.split("#",-1);
                if(parts.length!=body.blankCount){result.put("reason","blank-count-mismatch");return result;}
                List<String> values=new ArrayList<>();
                for(String part:parts){String value=Jsoup.parse(part).text().trim();if(value.isEmpty() || value.length()>12000){result.put("reason","blank-count-mismatch");return result;}values.add(value);}
                result.put("values",values);
            }
            result.put("matched",true);
        }catch(IOException missingOrUncertain){result.put("reason",homeworkFailureReason(missingOrUncertain));}
        return result;
    }

    private static String homeworkFailureReason(IOException error) {
        String message=error.getMessage()==null?"":error.getMessage();
        // Return categories only; provider errors can include URLs or private keys.
        if(message.contains("冲突"))return "answer-conflict";
        if(message.contains("选项")||message.contains("答案数量")||message.contains("判断题答案"))return "option-mismatch";
        if(message.contains("未查到"))return "no-match";
        if(message.contains("不支持"))return "unsupported-type";
        return "provider-error";
    }

    @ExceptionHandler(Exception.class)
    @ResponseStatus(HttpStatus.BAD_REQUEST)
    public Map<String,String> failure(Exception error) {
        String message=error instanceof ResponseStatusException ? ((ResponseStatusException)error).getReason() : error.getMessage();
        return Collections.singletonMap("message",message==null ? "测验读取或题库查询失败，未提交" : message);
    }
}
