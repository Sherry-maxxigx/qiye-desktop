package com.tihai.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import okhttp3.RequestBody;
import org.junit.jupiter.api.Test;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.jupiter.api.Assertions.*;

class LocalBrowserSessionTest {
    private static final String COOKIES="_uid=123456789; _d=fixture; vc3=fixture";
    private static final String COURSE="<div class='course'><input class='clazzId' value='102'/><input class='courseId' value='101'/><a href='?cpi=103&x=1'></a><span class='course-name' title='测试课程'></span></div>";
    private static class Fixture extends LocalBrowserSession {
        String profile="{\"status\":true,\"message\":{\"uid\":\"123456789\",\"phone\":\"13800000000\",\"realname\":\"测试\"}}";
        String interaction="", page=COURSE; AtomicInteger calls=new AtomicInteger();
        Fixture(){super(new ObjectMapper());}
        @Override protected String read(String url,String cookies,RequestBody body,long deadline) throws IOException {
            assertTrue(url.startsWith("https://"));assertTrue(url.contains(".chaoxing.com/"));calls.incrementAndGet();
            if(url.endsWith("getAccountMessage"))return profile;
            if(url.endsWith("interaction"))return interaction;
            return page;
        }
    }
    @Test void verifiedIdentityAndCourseDetailsAreRetainedWithoutPassword() throws Exception {
        Fixture fixture=new Fixture();LocalBrowserSession.Session result=fixture.verify(COOKIES,COOKIES);
        assertEquals("123456789",result.uid);assertEquals("13800000000",result.phone);
        assertEquals(1,result.courses.size());assertEquals("101",result.courses.get(0).getCourseId());
        assertEquals("103",result.courses.get(0).getCpi());assertTrue(result.storedCookies.contains("_uid=123456789;"));
    }
    @Test void unconfirmedOrMismatchedIdentityNeverFetchesCourses() {
        for(String profile:Arrays.asList("{\"status\":false}","{\"status\":true,\"message\":{\"uid\":\"99999\"}}","{}")) {
            Fixture fixture=new Fixture();fixture.profile=profile;
            assertThrows(IOException.class,()->fixture.verify(COOKIES,COOKIES));assertEquals(1,fixture.calls.get());
        }
    }
    @Test void malformedCookieHeadersAreRejectedBeforeAnyNetworkCall() {
        for(String cookies:Arrays.asList("", "_uid=x", "_uid=12345\r\nother: injected", "_uid=12345; _uid=67890", "_uid=12345,abc")) {
            Fixture fixture=new Fixture();assertThrows(IOException.class,()->fixture.verify(COOKIES,cookies));assertEquals(0,fixture.calls.get());
        }
    }
    @Test void foldersAreIncludedAndDuplicatesAreMerged() throws Exception {
        Fixture fixture=new Fixture();fixture.interaction="<ul class='file-list'><li fileid='88'></li></ul>";
        assertEquals(1,fixture.verify(COOKIES,COOKIES).courses.size());assertEquals(4,fixture.calls.get());
    }
    @Test void expiredCoursePageCannotBecomeSuccessfulEmptyCourseList() {
        Fixture fixture=new Fixture();fixture.page="<button id=\"loginBtn\">登录</button>";
        assertThrows(IOException.class,()->fixture.verify(COOKIES,COOKIES));
    }
    @Test void authenticatedAccountMayHaveNoCourses() throws Exception {
        Fixture fixture=new Fixture();fixture.page="<div class='empty-course'>暂无课程</div>";
        assertTrue(fixture.verify(COOKIES,COOKIES).courses.isEmpty());
    }
    @Test void courseReadsOverlapWithBoundedConcurrencyAndStableOrdering() throws Exception {
        AtomicInteger active=new AtomicInteger(), peak=new AtomicInteger(), requests=new AtomicInteger();
        Fixture fixture=new Fixture() {
            @Override protected String read(String url,String cookies,RequestBody body,long deadline) throws IOException {
                if(url.endsWith("getAccountMessage"))return super.read(url,cookies,body,deadline);
                int concurrent=active.incrementAndGet();peak.accumulateAndGet(concurrent,Math::max);requests.incrementAndGet();
                try {
                    try {Thread.sleep(120);}catch(InterruptedException interrupted){Thread.currentThread().interrupt();throw new IOException("canceled");}
                    if(url.endsWith("interaction"))return "<ul class='file-list'><li fileid='1'></li><li fileid='2'></li><li fileid='3'></li><li fileid='4'></li><li fileid='5'></li><li fileid='6'></li><li fileid='2'></li><li fileid='0'></li></ul>";
                    String id=((okhttp3.FormBody)body).value(1);
                    return COURSE.replace("value='101'","value='10"+id+"'");
                } finally {active.decrementAndGet();}
            }
        };
        long started=System.nanoTime();SessionResult result=new SessionResult(fixture.verify(COOKIES,COOKIES));
        long elapsed=(System.nanoTime()-started)/1000000;
        assertTrue(peak.get()>1);assertTrue(peak.get()<=4);assertEquals(8,requests.get());assertEquals(0,active.get());
        assertEquals(Arrays.asList("100","101","102","103","104","105","106"),result.ids);
        assertTrue(elapsed<800,"Independent reads should not take the serial 960 ms: "+elapsed);
        System.out.println("Course-read fixture: serial work=960ms, parallel elapsed="+elapsed+"ms, peak="+peak.get());
    }
    private static final class SessionResult {
        final List<String> ids=new ArrayList<>();
        SessionResult(LocalBrowserSession.Session session){for(com.tihai.dubbo.pojo.course.Course course:session.courses)ids.add(course.getCourseId());}
    }
    @Test void folderFailureDoesNotBecomePartialSuccessfulLogin() {
        Fixture fixture=new Fixture() {
            @Override protected String read(String url,String cookies,RequestBody body,long deadline) throws IOException {
                if(url.endsWith("interaction"))return "<ul class='file-list'><li fileid='88'></li></ul>";
                if(body!=null&&((okhttp3.FormBody)body).value(1).equals("88"))throw new IOException("synthetic folder failure");
                return super.read(url,cookies,body,deadline);
            }
        };
        assertEquals("synthetic folder failure",assertThrows(IOException.class,()->fixture.verify(COOKIES,COOKIES)).getMessage());
    }
}
