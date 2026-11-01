package com.tihai.utils;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.Question;
import com.tihai.config.TiKuConfig;
import org.springframework.stereotype.Component;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;

/** Reads optional local data, requiring the same stem, type and full option set. */
@Component
public class LocalQuestionBank {
    private static final ObjectMapper JSON=new ObjectMapper();
    private final TiKuConfig config;
    private volatile Snapshot cached;
    private static final class Snapshot {
        final Path path;final long size,modified;final Map<String,List<Row>> index;
        Snapshot(Path p,long s,long m,Map<String,List<Row>> i){path=p;size=s;modified=m;index=i;}
    }
    private static final class Row {
        String type,title,answer;Set<String> options;
    }
    public static final class NoMatchException extends IOException {
        NoMatchException(){super("本地题库未查到相同题干、题型和选项的答案");}
    }
    public static final class ConflictException extends IOException {
        ConflictException(){super("本地题库返回互相冲突的答案；题目留空");}
    }
    public LocalQuestionBank(TiKuConfig config){this.config=config;}

    static String stem(String raw){
        return QuizAnswers.title(raw).replaceAll("\\s+","").replaceAll(":?\\(\\)","()")
                .replaceAll("[。.!！?？]+$","");
    }
    private static String option(String raw){return QuizAnswers.text(raw).replaceAll("\\s+","");}
    private static String truth(String raw){
        String value=QuizAnswers.text(raw).toLowerCase(Locale.ROOT);
        if(Arrays.asList("正确","对","是","true","√").contains(value))return "true";
        if(Arrays.asList("错误","错","否","false","×").contains(value))return "false";
        return null;
    }
    private static Row parse(JsonNode value){
        if(!value.isObject())return null;
        Row row=new Row();row.type=value.path("type").asText();
        if("judge".equals(row.type))row.type="judgement";
        if(!Arrays.asList("single","multiple","judgement").contains(row.type))return null;
        row.title=stem(value.path("question").asText());
        if(row.title.length()<2||row.title.length()>4000||row.title.contains("空题"))return null;
        JsonNode correct=value.path("correct_answers");
        if(!correct.isArray()||correct.isEmpty())return null;
        if("judgement".equals(row.type)){
            if(correct.size()!=1||(row.answer=truth(correct.get(0).asText()))==null)return null;
            row.options=Collections.emptySet();return row;
        }
        JsonNode options=value.path("options");
        if(!options.isArray()||options.size()<2||options.size()>26)return null;
        Map<String,String> labels=new LinkedHashMap<>();row.options=new HashSet<>();
        for(JsonNode item:options){
            String key=item.path("key").asText(),text=QuizAnswers.text(item.path("content").asText());
            if(!key.matches("[A-Z]")||labels.containsKey(key)||text.isEmpty()||text.length()>4000||text.contains("#")||!row.options.add(option(text)))return null;
            labels.put(key,text);
        }
        List<String> answers=new ArrayList<>();Set<String> keys=new HashSet<>();
        for(JsonNode key:correct){String label=key.asText();if(!labels.containsKey(label)||!keys.add(label))return null;answers.add(labels.get(label));}
        if("single".equals(row.type)&&answers.size()!=1)return null;
        row.answer=String.join("#",answers);return row;
    }
    private synchronized Snapshot snapshot() throws IOException {
        String name=config.getLocalBankFile();if(name==null||name.trim().isEmpty())throw new NoMatchException();
        final Path path;
        try{path=Paths.get(name).toAbsolutePath().normalize();}catch(InvalidPathException failure){throw new IOException("本地题库路径无效");}
        long size=Files.size(path),modified=Files.getLastModifiedTime(path).toMillis();
        if(size<2||size>8*1024*1024)throw new IOException("本地题库文件大小无效");
        Snapshot current=cached;
        if(current!=null&&current.path.equals(path)&&current.size==size&&current.modified==modified)return current;
        JsonNode body=JSON.readTree(Files.readAllBytes(path));
        JsonNode questions=body!=null&&body.isArray()?body:body==null?null:body.path("questions");
        if(questions==null||!questions.isArray()||questions.size()>20000)throw new IOException("本地题库格式无效");
        Map<String,List<Row>> index=new HashMap<>();
        for(JsonNode value:questions){Row row=parse(value);if(row!=null)index.computeIfAbsent(row.type+"|"+row.title,key->new ArrayList<>()).add(row);}
        return cached=new Snapshot(path,size,modified,index);
    }
    public String queryAnswer(Question question) throws IOException {
        if(question==null||!Arrays.asList("single","multiple","judgement").contains(question.getType()))throw new NoMatchException();
        List<Row> rows=snapshot().index.get(question.getType()+"|"+stem(question.getTitle()));
        if(rows==null)throw new NoMatchException();
        Set<String> options=new HashSet<>();
        if(!"judgement".equals(question.getType())){
            if(question.getTKOptions()==null||question.getTKOptions().size()<2)throw new NoMatchException();
            Set<String> labels=new HashSet<>();
            for(String raw:question.getTKOptions()){
                String label=QuizAnswers.text(raw);
                if(!label.matches("^[A-Z][.、:：)）\\s].*")||!labels.add(label.substring(0,1)))throw new NoMatchException();
                String key=option(label.substring(1).replaceFirst("^[.、:：)）\\s]+",""));
                if(key.isEmpty()||!options.add(key))throw new NoMatchException();
            }
        }
        Map<String,String> candidates=new HashMap<>();
        for(Row row:rows){
            if(!"judgement".equals(row.type)&&!row.options.equals(options))continue;
            try{candidates.put(QuizAnswers.resolve(question,row.answer),row.answer);}catch(IOException mismatch){/* Keep unmatched candidates out. */}
        }
        if(candidates.size()>1)throw new ConflictException();
        if(candidates.isEmpty())throw new NoMatchException();
        return candidates.values().iterator().next();
    }
}
