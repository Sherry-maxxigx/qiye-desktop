package com.tihai.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.CourseFolder;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.utils.Decode;
import okhttp3.*;
import org.springframework.context.annotation.Profile;
import org.springframework.stereotype.Service;

import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;

/** Verify an official browser login without a password or returning session data to the UI. */
@Service
@Profile("local")
public class LocalBrowserSession {
    private final ObjectMapper json;
    private final OkHttpClient client = new OkHttpClient.Builder().followRedirects(false)
            .connectTimeout(6, TimeUnit.SECONDS).readTimeout(10, TimeUnit.SECONDS).build();

    public LocalBrowserSession(ObjectMapper json) { this.json = json; }

    public static final class Session {
        public String uid, phone, name, storedCookies;
        public List<Course> courses;
    }

    public Session verify(String passportCookies, String courseCookies) throws IOException {
        Map<String, String> coursePairs = cookiePairs(courseCookies);
        cookiePairs(passportCookies);
        String uid = coursePairs.get("_uid");
        if (uid == null || !uid.matches("\\d{1,20}")) { throw new IOException("请先在学习通手机 App 中扫码并确认登录"); }
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(35);
        JsonNode profile;
        try { profile = json.readTree(read("https://passport2.chaoxing.com/mooc/getAccountMessage", passportCookies, null, deadline)); }
        catch (IOException failure) { throw new IOException("官方登录状态读取失败，请刷新二维码后重试"); }
        JsonNode message = profile == null ? json.createObjectNode() : profile.path("message");
        if (profile == null || !profile.path("status").asBoolean() || !uid.equals(message.path("uid").asText())) {
            throw new IOException("官方尚未确认登录，请在手机上确认后再试");
        }
        Session session = new Session();
        session.uid = uid; session.phone = message.path("phone").asText(""); session.name = message.path("realname").asText("");
        List<String> stored = new ArrayList<>();
        for (Map.Entry<String,String> pair : coursePairs.entrySet()) { stored.add(pair.getKey()+"="+pair.getValue()+"; domain=chaoxing.com; path=/; secure"); }
        session.storedCookies = String.join(", ", stored);
        Map<String,Course> unique = new LinkedHashMap<>();
        // Verify identity before starting any course request. Read independent pages
        // together, with one deadline and at most four connections for this login.
        ExecutorService reads = Executors.newFixedThreadPool(4, runnable -> {
            Thread worker = new Thread(runnable, "desktop-course-read");
            worker.setDaemon(true); return worker;
        });
        List<Future<?>> pending = new ArrayList<>();
        try {
            Future<List<Course>> main = reads.submit(() -> readCourses("0", courseCookies, deadline));
            Future<String> folders = reads.submit(() -> read("https://mooc2-ans.chaoxing.com/mooc2-ans/visit/interaction", courseCookies, null, deadline));
            pending.add(main); pending.add(folders);
            List<Future<List<Course>>> pages = new ArrayList<>();
            Set<String> seen = new HashSet<>(); seen.add("0");
            for (CourseFolder folder : Decode.decodeCourseFolder(await(folders, deadline))) {
                String id = folder.getId();
                if (id == null || !id.matches("\\d{1,20}") || !seen.add(id)) { continue; }
                Future<List<Course>> page = reads.submit(() -> readCourses(id, courseCookies, deadline));
                pages.add(page); pending.add(page);
            }
            collect(unique, await(main, deadline));
            // Preserve the official folder order even when requests finish out of order.
            for (Future<List<Course>> page : pages) { collect(unique, await(page, deadline)); }
        } finally {
            for (Future<?> request : pending) { if (!request.isDone()) request.cancel(true); }
            reads.shutdownNow();
        }
        session.courses = new ArrayList<>(unique.values());
        return session;
    }

    private static <T> T await(Future<T> pending, long deadline) throws IOException {
        long remaining = deadline - System.nanoTime();
        if (remaining <= 0) { throw new IOException("读取课程超时，请点击重试同步"); }
        try { return pending.get(remaining, TimeUnit.NANOSECONDS); }
        catch (InterruptedException failure) {
            Thread.currentThread().interrupt(); throw new IOException("课程读取已取消");
        } catch (TimeoutException failure) { throw new IOException("读取课程超时，请点击重试同步"); }
        catch (ExecutionException failure) {
            if (failure.getCause() instanceof IOException) { throw (IOException) failure.getCause(); }
            throw new IOException("课程读取未完成，请点击重试同步");
        }
    }

    private List<Course> readCourses(String folder, String cookies, long deadline) throws IOException {
        RequestBody form = new FormBody.Builder().add("courseType","1").add("courseFolderId",folder)
                .add("Query", "").add("superstarClass", "0").build();
        String page = read("https://mooc2-ans.chaoxing.com/mooc2-ans/visit/courselistdata", cookies, form, deadline);
        if (page.contains("id=\"loginBtn\"") || page.contains("passport2.chaoxing.com/login")) {
            throw new IOException("课程登录已失效，请重新扫码登录");
        }
        return Decode.decodeCourseList(page);
    }

    private void collect(Map<String,Course> target, List<Course> courses) {
        for (Course course : courses) {
            if (course.getCourseId().matches("\\d+") && course.getClazzId().matches("\\d+") && !course.getTitle().isEmpty()) {
                target.put(course.getCourseId()+":"+course.getClazzId(), course);
            }
        }
    }

    protected String read(String url, String cookies, RequestBody body, long deadline) throws IOException {
        long remaining = TimeUnit.NANOSECONDS.toMillis(deadline-System.nanoTime());
        if (remaining <= 0) { throw new IOException("读取课程超时，请点击重试同步"); }
        Request.Builder request = new Request.Builder().url(url).header("Cookie",cookies)
                .header("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36")
                .header("Referer","https://i.chaoxing.com/");
        if (body != null) { request.post(body).header("X-Requested-With","XMLHttpRequest"); }
        Call call = client.newCall(request.build());
        call.timeout().timeout(Math.min(10000,remaining),TimeUnit.MILLISECONDS);
        try (Response response = call.execute()) {
            if (!response.isSuccessful() || response.body() == null) { throw new IOException("官方页面读取失败，请稍后重试同步"); }
            return response.body().string();
        } catch (IOException failure) { throw new IOException("官方连接失败或超时，请检查网络后重试同步"); }
    }

    static Map<String,String> cookiePairs(String header) throws IOException {
        if (header == null || header.isEmpty() || header.length()>60000 || header.contains("\r") || header.contains("\n")) {
            throw new IOException("登录状态无效，请重新扫码");
        }
        Map<String,String> result = new LinkedHashMap<>();
        for (String part : header.split(";")) {
            int separator = part.indexOf('=');
            if (separator <= 0) { throw new IOException("登录状态格式无效，请重新扫码"); }
            String name=part.substring(0,separator).trim(), value=part.substring(separator+1).trim();
            if (!name.matches("[A-Za-z0-9_-]+") || value.contains(",") || result.containsKey(name)) {
                throw new IOException("登录状态格式无效，请重新扫码");
            }
            result.put(name,value);
        }
        return result;
    }
}
