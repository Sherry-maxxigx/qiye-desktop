package com.tihai.utils;

import com.alibaba.nacos.api.exception.NacosException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.common.*;
import com.tihai.manager.StudyTaskControl;
import com.tihai.service.LocalStudyProgress;
import com.tihai.common.GlobalConst;
import com.tihai.domain.chaoxing.SuperStarLog;
import com.tihai.domain.chaoxing.WkUser;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.service.superstar.SuperStarCookieService;
import com.tihai.service.superstar.SuperStarLogService;
import okhttp3.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.time.LocalDateTime;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ThreadLocalRandom;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import javax.net.ssl.*;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

/**
 * @Copyright : DuanInnovator
 * @Description : 课程工具
 * @Author : DuanInnovator
 * @CreateTime : 2025/3/1
 * @Link : <a href="https://github.com/DuanInnovator/SuperAutotudy">...</a>
 **/
@Component
public class CourseUtil {


    public Map<String, String> cookies;
    public List<Cookie> cookieList = new ArrayList<>();

    @Autowired
    private StudyTaskControl taskControl;

    @Autowired(required = false)
    private LocalStudyProgress localProgress;

    private String account;

    @Autowired
    private SuperStarLogService superStarLogService;

    @Autowired
    private SuperStarCookieService cookieService;

    @Autowired
    private ServerInfoUtil serverInfoUtil;

    @Autowired
    private Query query;

    public void setAccount(String account) {
        this.account = account;
    }


    public void setCookies(String cookies) {
        this.cookies = convertCookieStringToMap(cookies);
        cookieList = convertCookieStringToList(cookies);
    }

    /**
     * 将 Cookie 字符串转换为 Cookie 列表
     *
     * @param cookieStr Cookie 字符串
     * @return Cookie 列表
     */
    public static List<Cookie> convertCookieStringToList(String cookieStr) {
        List<Cookie> cookies = new ArrayList<>();

        if (cookieStr == null || cookieStr.isEmpty()) {
            return cookies;
        }
        String[] cookieArray = cookieStr.split(", ");
        for (String cookieEntry : cookieArray) {
            String[] parts = cookieEntry.split(";");
            if (parts.length > 0) {
                String[] keyValue = parts[0].split("=", 2);
                if (keyValue.length == 2) {
                    Cookie cookie = new Cookie.Builder()
                            .name(keyValue[0].trim())
                            .value(keyValue[1].trim())
                            .domain("chaoxing.com")
                            .path("/")
                            .build();
                    cookies.add(cookie);
                }
            }
        }
        return cookies;
    }

    public static Map<String, String> convertCookieStringToMap(String cookieStr) {
        // 去掉前后的方括号
        if (cookieStr.startsWith("[") && cookieStr.endsWith("]")) {
            cookieStr = cookieStr.substring(1, cookieStr.length() - 1);
        }
        Map<String, String> cookieMap = new LinkedHashMap<>();
        String[] cookies = cookieStr.split(", ");


        for (String cookie : cookies) {
            String[] attributes = cookie.split(";");
            String[] keyValue = attributes[0].split("=", 2); // 只取第一个键值对，防止 value 中带 "=" 导致错误

            if (keyValue.length == 2) {
                String key = keyValue[0].trim();
                String value = keyValue[1].trim();

                // 如果 key 已存在，合并值，确保所有相同 key 的值都能保留
                if (cookieMap.containsKey(key)) {
                    cookieMap.put(key, cookieMap.get(key) + "," + value);
                } else {
                    cookieMap.put(key, value);
                }
            }
        }
        return cookieMap;
    }

    public String getValue(String key) {
        return cookies.get(key);
    }


    /**
     * 获取随机秒数
     *
     * @return 随机秒数
     */
    public static int getRandomSeconds() {
        return ThreadLocalRandom.current().nextInt(100, 120);
    }

    /**
     * 获取当前时间戳
     *
     * @return 时间戳字符串
     */
    public static String getTimestamp() {
        return String.valueOf(System.currentTimeMillis());
    }


    /**
     * 初始化 OkHttpClient，并根据 isVideo / isAudio 设置请求头
     */

    public OkHttpClient initSession(boolean isVideo, boolean isAudio) {
        // 创建信任所有证书的 TrustManager
        TrustManager[] trustAllCerts = new TrustManager[]{
                new X509TrustManager() {
                    @Override
                    public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    }

                    @Override
                    public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    }

                    @Override
                    public X509Certificate[] getAcceptedIssuers() {
                        return new X509Certificate[0];
                    }
                }
        };

        // 创建 SSLContext 并使用 TrustManager 初始化
        SSLContext sslContext;
        try {
            sslContext = SSLContext.getInstance("SSL");
            sslContext.init(null, trustAllCerts, new java.security.SecureRandom());
        } catch (Exception e) {
            throw new RuntimeException(e);
        }

        // 创建 OkHttpClient.Builder
        OkHttpClient.Builder builder = new OkHttpClient.Builder()
                .retryOnConnectionFailure(true); // 启用重试机制
//                .sslSocketFactory(sslContext.getSocketFactory(), (X509TrustManager) trustAllCerts[0]) // 禁用 SSL 验证
//                .hostnameVerifier((hostname, session) -> true); // 禁用主机名验证

        // 配置 CookieJar
        builder.cookieJar(new CookieJar() {
            private final Map<String, List<Cookie>> cookieCache = new ConcurrentHashMap<>();

            @Override
            public void saveFromResponse(HttpUrl url, List<Cookie> cookies) {
                String domain = url.host();

                // 获取现有Cookie（从内存或数据库）
                List<Cookie> existingCookies = cookieCache.computeIfAbsent(domain, k -> {
                    String cookieStr = cookieService.getWkUserCookie(account);
                    return parseCookieString(cookieStr);
                });

                // 检测是否有变更
                boolean changed = false;
                for (Cookie newCookie : cookies) {
                    boolean exists = existingCookies.stream()
                            .anyMatch(c -> c.name().equals(newCookie.name()) &&
                                    c.value().equals(newCookie.value()));

                    if (!exists) {
                        existingCookies.removeIf(c -> c.name().equals(newCookie.name()));
                        existingCookies.add(newCookie);
                        changed = true;
                    }
                }

                // 如果有变更则更新数据库
                if (changed) {
                    String formattedCookies = formatCookies(existingCookies);
                    WkUser user = new WkUser();
                    user.setAccount(account);
                    user.setCookies(formattedCookies);
                    cookieService.updateWkUserCookies(user);
                }
            }

            // 格式化Cookie为[ ]包裹的字符串
            // 格式化Cookie为字符串（去除[ ]）
            private String formatCookies(List<Cookie> cookies) {
                if (cookies == null || cookies.isEmpty()) {
                    return "";  // 返回空字符串
                }

                return cookies.stream()
                        .map(c -> {
                            StringBuilder sb = new StringBuilder();
                            sb.append(c.name()).append("=").append(c.value());

                            if (c.expiresAt() != Long.MAX_VALUE) {
                                sb.append("; expires=").append(formatDate(c.expiresAt()));
                            }
                            if (c.domain() != null) {
                                sb.append("; domain=").append(c.domain());
                            }
                            if (c.path() != null) {
                                sb.append("; path=").append(c.path());
                            }
                            if (c.secure()) {
                                sb.append("; secure");
                            }
                            if (c.httpOnly()) {
                                sb.append("; httponly");
                            }
                            return sb.toString();
                        })
                        .collect(Collectors.joining(", "));  // 直接拼接字符串，不加方括号
            }

            // 日期格式化（RFC 1123格式）
            private String formatDate(long timestamp) {
                SimpleDateFormat sdf = new SimpleDateFormat(
                        "EEE, dd MMM yyyy HH:mm:ss z", Locale.US);
                sdf.setTimeZone(TimeZone.getTimeZone("GMT"));
                return sdf.format(new Date(timestamp));
            }

            // 从字符串解析Cookie列表
            // 从字符串解析Cookie列表
            private List<Cookie> parseCookieString(String cookieStr) {
                if (cookieStr == null || cookieStr.trim().isEmpty()) {
                    return new ArrayList<>();
                }

                // 处理空内容
                String content = cookieStr.trim();
                if (content.isEmpty()) {
                    return new ArrayList<>();
                }

                // 处理包含逗号的cookie值
                return Arrays.stream(content.split(",(?=\\s*[^=]+=)"))
                        .map(String::trim)
                        .map(this::parseSingleCookie)
                        .filter(Objects::nonNull)
                        .collect(Collectors.toList());
            }


            private Cookie parseSingleCookie(String cookieStr) {
                try {
                    String[] parts = cookieStr.split(";\\s*");
                    String[] nameValue = parts[0].split("=", 2);
                    if (nameValue.length != 2) return null;

                    Cookie.Builder builder = new Cookie.Builder()
                            .name(nameValue[0].trim())
                            .value(nameValue[1].trim());

                    for (int i = 1; i < parts.length; i++) {
                        String attr = parts[i].trim().toLowerCase();
                        if (attr.startsWith("domain=")) {
                            builder.domain(attr.substring(7));
                        } else if (attr.startsWith("path=")) {
                            builder.path(attr.substring(5));
                        } else if (attr.startsWith("expires=")) {
                            try {
                                SimpleDateFormat sdf = new SimpleDateFormat(
                                        "EEE, dd MMM yyyy HH:mm:ss z", Locale.US);
                                Date date = sdf.parse(attr.substring(8));
                                builder.expiresAt(date.getTime());
                            } catch (ParseException e) {
                                // 忽略日期解析错误
                            }
                        } else if (attr.equals("secure")) {
                            builder.secure();
                        } else if (attr.equals("httponly")) {
                            builder.httpOnly();
                        }
                    }
                    return builder.build();
                } catch (Exception e) {
                    return null;
                }
            }

            @Override
            public List<Cookie> loadForRequest(HttpUrl url) {

                try {
                    // 1. 检查基础条件
                    if (url == null || account == null || cookieService == null) {
                        System.out.println("获取Cookie异常: 参数错误 -> " +
                                "url=" + url + ", " +
                                "account=" + account + ", " +
                                "cookieService=" + cookieService);
                        return Collections.emptyList();
                    }

                    // 2. 内存缓存检查
                    String domain = url.host();
                    if (cookieCache.containsKey(domain)) {
                        return new ArrayList<>(cookieCache.get(domain)); // 返回副本避免并发修改
                    }

                    // 3. 安全获取Cookie
                    String wkUserCookie = Optional.ofNullable(cookieService)
                            .map(service -> service.getWkUserCookie(account))
                            .orElse("");

                    if (!wkUserCookie.isEmpty()) {
                        List<Cookie> cookies = convertCookieStringToList(wkUserCookie);
                        cookieCache.put(domain, new CopyOnWriteArrayList<>(cookies));
                        return cookies;
                    }
                } catch (Exception e) {
                    System.out.println("获取Cookie异常: " + e.getMessage());
                }
                return Collections.emptyList();
            }

        });

        // 使用拦截器添加统一请求头
        builder.addInterceptor(chain -> {
            taskControl.checkpoint();
            Request original = chain.request();
            Request.Builder reqBuilder = original.newBuilder();
            Map<String, String> headers;
            if (isVideo) {
                headers = GlobalConst.VIDEO_HEADERS;
            } else if (isAudio) {
                headers = GlobalConst.AUDIO_HEADERS;
            } else {
                headers = GlobalConst.HEADERS;
            }
            headers.forEach(reqBuilder::header);
            Request req = reqBuilder.build();
            return chain.proceed(req);
        });

        return builder.build();
    }


    /**
     * 获取课程列表请求头
     *
     * @return 请求头
     */
    private Map<String, String> getCourseListHeaders() {
        Map<String, String> headers = new HashMap<>();
        headers.put("Host", "mooc2-ans.chaoxing.com");
        headers.put("sec-ch-ua-platform", "\"Windows\"");
        headers.put("X-Requested-With", "XMLHttpRequest");
        headers.put("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ...");
        headers.put("Accept", "text/html, */*; q=0.01");
        headers.put("sec-ch-ua", "\"Microsoft Edge\";v=\"129\", \"Not=A?Brand\";v=\"8\", \"Chromium\";v=\"129\"");
        headers.put("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        headers.put("sec-ch-ua-mobile", "?0");
        headers.put("Origin", "https://mooc2-ans.chaoxing.com");
        headers.put("Sec-Fetch-Site", "same-origin");
        headers.put("Sec-Fetch-Mode", "cors");
        headers.put("Sec-Fetch-Dest", "empty");
        headers.put("Referer", "https://mooc2-ans.chaoxing.com/mooc2-ans/visit/interaction?moocDomain=https://mooc1-1.chaoxing.com/mooc-ans");
        headers.put("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6,ja;q=0.5");
        return headers;
    }


    /**
     * 获取课程列表
     */
    public List<Course> getCourseList() throws IOException {
        OkHttpClient client = initSession(false, false);
        String url = "https://mooc2-ans.chaoxing.com/mooc2-ans/visit/courselistdata";
        FormBody formBody = new FormBody.Builder()
                .add("courseType", "1")
                .add("courseFolderId", "0")
                .add("Query", "")
                .add("superstarClass", "0")
                .build();
        // 此处定义专用的请求头
        Request request = new Request.Builder()
                .url(url)
                .post(formBody)
                .headers(Headers.of(getCourseListHeaders()))
                .build();


        Response response = client.newCall(request).execute();

        String respText = response.body().string();
        List<Course> courseList = Decode.decodeCourseList(respText);

        // 处理课程文件夹
        String interactionUrl = "https://mooc2-ans.chaoxing.com/mooc2-ans/visit/interaction";
        Request interRequest = new Request.Builder().url(interactionUrl).build();
        Response interResponse = client.newCall(interRequest).execute();
        String interText = interResponse.body().string();
        List<CourseFolder> courseFolder = Decode.decodeCourseFolder(interText);
        for (CourseFolder folder : courseFolder) {
            FormBody folderForm = new FormBody.Builder()
                    .add("courseType", "1")
                    .add("courseFolderId", folder.getId())
                    .add("Query", "")
                    .add("superstarClass", "0")
                    .build();
            Request folderRequest = new Request.Builder()
                    .url(url)
                    .post(folderForm)
                    .build();
            Response folderResponse = client.newCall(folderRequest).execute();
            String folderText = folderResponse.body().string();

            courseList.addAll(Decode.decodeCourseList(folderText));
        }
        return courseList;
    }


    /**
     * 获取课程章节（course point）
     */
    public List<CoursePoint> getCoursePoint(String courseId, String clazzId, String cpi) throws IOException {
        OkHttpClient client = initSession(false, false);
        String url = String.format("https://mooc2-ans.chaoxing.com/mooc2-ans/mycourse/studentcourse?courseid=%s&clazzid=%s&cpi=%s&ut=s",
                courseId, clazzId, cpi);
        Request request = new Request.Builder().url(url).addHeader("Cookie", cookieList.toString()).build();
        Response response = client.newCall(request).execute();
        String respText = response.body().string();
        CoursePoint stringObjectMap = Decode.decodeCoursePoint(respText);
        return Arrays.asList(Decode.decodeCoursePoint(respText));
    }

    /**
     * 获取任务点列表
     */
    public Pair<List<Job>, List<JobInfo>> getJobList(String clazzId, String courseId, String cpi, String knowledgeId) throws IOException {
        OkHttpClient client = initSession(false, false);
        List<Job> jobList = new ArrayList<>();
        List<JobInfo> jobInfo = new ArrayList<>();
        String[] possibleNums = {"0", "1", "2"};
        for (String num : possibleNums) {
            String url = String.format("https://mooc1.chaoxing.com/mooc-ans/knowledge/cards?clazzid=%s&courseid=%s&knowledgeid=%s&num=%s&ut=s&cpi=%s&v=20160407-3&mooc2=1",
                    clazzId, courseId, knowledgeId, num, cpi);
            Request request = new Request.Builder().url(url).build();
            Response response = client.newCall(request).execute();
            String respText = response.body().string();
            Pair<List<Job>, JobInfo> result = Decode.decodeCourseCard(respText);
            List<Job> _jobList = result.getFirst();

            List<JobInfo> _jobInfo = Arrays.asList(result.getSecond());
            if (Boolean.TRUE.equals(_jobInfo.get(0).getNotOpen())) {
                return new Pair<>(new ArrayList<>(), _jobInfo);
            }
            jobList.addAll(_jobList);
            jobInfo.addAll(_jobInfo);
        }
        return new Pair<>(jobList, jobInfo);
    }

    /**
     * 生成 MD5 加密串
     */
    public static String getEnc(String clazzId, String jobId, String objectId, long playingTime, long duration, String userId)
            throws Exception {
        String str = String.format("[%s][%s][%s][%s][%d][d_yHJ!$pdA~5][%d][0_%s]",
                clazzId, userId, jobId, objectId, playingTime * 1000, duration * 1000, duration);
        MessageDigest md = MessageDigest.getInstance("MD5");
        md.update(str.getBytes());
        byte[] digest = md.digest();
        StringBuilder sb = new StringBuilder();
        for (byte b : digest) {
            sb.append(String.format("%02x", b));
        }
        return sb.toString();
    }

    /**
     * 视频播放进度日志
     */
    public Boolean videoProgressLog(OkHttpClient session, Course course,
                                    Job job, JobInfo jobInfo, String dtoken, long duration,
                                    long playingTime, String type, int eventType) throws Exception {
        String respText = null;
        int statusCode = 0;
        HttpUrl url = createVideoProgressUrl(course, job, dtoken, duration, playingTime, type, getValue("_uid"), eventType);
        Request request = new Request.Builder().url(url).build();
        try (Response response = session.newCall(request).execute()) {
            statusCode = response.code();
            respText = response.body() == null ? null : response.body().string();
        }
        boolean passed = parseVideoProgressResponse(statusCode, respText);
        System.out.printf("视频进度响应: 课程=%s 视频=%s 事件=%d 上报=%d/%d秒 平台确认完成=%s%n",
                course.getTitle(), job.getName(), eventType, playingTime, duration, passed);
        return passed;
    }

    static int videoEventType(boolean firstReport, long position, long duration) {
        return firstReport ? 3 : position >= duration ? 4 : 0;
    }

    static HttpUrl createVideoProgressUrl(Course course, Job job, String dtoken, long duration,
                                          long position, String type, String userId, int eventType) throws Exception {
        String otherInfo = job.getOtherInfo() == null ? "" : job.getOtherInfo();
        String midText = "otherInfo=" + otherInfo + "&";
        if (!otherInfo.contains("courseId")) { midText += "courseId=" + course.getCourseId() + "&"; }
        String rawUrl = String.format("https://mooc1.chaoxing.com/mooc-ans/multimedia/log/a/%s/%s?clazzId=%s&playingTime=%d"
                        + "&duration=%d&clipTime=0_%d&objectId=%s&%sjobid=%s&userid=%s&isdrag=%d&view=pc&enc=%s&rt=1&dtype=%s&_t=%s",
                course.getCpi(), dtoken, course.getClazzId(), position, duration, duration, job.getObjectId(), midText,
                job.getJobId(), userId, eventType, getEnc(course.getClazzId(), job.getJobId(), job.getObjectId(), position, duration, userId),
                type, getTimestamp());
        HttpUrl parsed = HttpUrl.parse(rawUrl);
        if (parsed == null) { throw new IOException("视频进度地址无效"); }
        HttpUrl.Builder builder = parsed.newBuilder();
        if (job.getAttDuration() != null) { builder.addQueryParameter("attDuration", job.getAttDuration().toString()); }
        if (job.getAttDurationEnc() != null && !job.getAttDurationEnc().isEmpty()) {
            builder.addQueryParameter("attDurationEnc", job.getAttDurationEnc());
        }
        if (job.getVideoFaceCaptureEnc() != null && !job.getVideoFaceCaptureEnc().isEmpty()) {
            builder.addQueryParameter("videoFaceCaptureEnc", job.getVideoFaceCaptureEnc());
        }
        return builder.build();
    }

    static boolean parseVideoProgressResponse(int statusCode, String body) throws IOException {
        if (statusCode != 200 || body == null) {
            throw new IOException("视频进度请求失败，HTTP=" + statusCode);
        }
        Map<String, Object> response = JsonParser.parse(body);
        if (response == null || !(response.get("isPassed") instanceof Boolean)) {
            throw new IOException("视频进度响应缺少平台完成状态");
        }
        if (Boolean.TRUE.equals(response.get("videoTimeLimit")) || Boolean.TRUE.equals(response.get("hasJobLimit"))) {
            throw new IOException("学习通返回视频或任务限制，请在官方课程页面查看");
        }
        return Boolean.TRUE.equals(response.get("isPassed"));
    }

    private Decode.VideoRecord readVideoRecord(OkHttpClient session, Course course, Job job, JobInfo jobInfo) throws IOException {
        for (int num = 0; num < 3; num++) {
            String url = String.format("https://mooc1.chaoxing.com/mooc-ans/knowledge/cards?clazzid=%s&courseid=%s&knowledgeid=%s&num=%d&ut=s&cpi=%s&v=20160407-3&mooc2=1",
                    course.getClazzId(), course.getCourseId(), jobInfo.getKnowledgeid(), num, course.getCpi());
            Request request = new Request.Builder().url(url).build();
            try (Response response = session.newCall(request).execute()) {
                if (!response.isSuccessful() || response.body() == null) {
                    throw new IOException("无法读取学习通播放位置，HTTP=" + response.code());
                }
                Decode.VideoRecord record = Decode.decodeVideoRecord(response.body().string(), job.getJobId());
                if (record != null) {
                    return record;
                }
            }
        }
        throw new IOException("无法核对学习通播放位置，已停止当前任务");
    }

    /**
     * 学习视频（模拟观看）
     */
    public void studyVideo(Course course, Job job, JobInfo jobInfo,
                           double speed, String type, SuperStarLog log) throws Exception {

        OkHttpClient session = ("Video".equals(type)) ?
                initSession(true, false) : initSession(false, true);
        // 获取视频信息
        String infoUrl = String.format("https://mooc1.chaoxing.com/ananas/status/%s?k=%s&flag=normal",
                job.getObjectId(), getValue("fid"));
        Request infoRequest = new Request.Builder().url(infoUrl).build();
        Map<String, Object> videoInfo;
        try (Response infoResponse = session.newCall(infoRequest).execute()) {
            if (!infoResponse.isSuccessful() || infoResponse.body() == null) {
                throw new IOException("视频信息读取失败，HTTP=" + infoResponse.code());
            }
            videoInfo = JsonParser.parse(infoResponse.body().string());
        }
        if (videoInfo != null && "success".equals(videoInfo.get("status"))) {
            String dtoken = videoInfo.get("dtoken").toString();
            long duration = Long.parseLong(videoInfo.get("duration").toString());
            if (duration <= 0) {
                throw new IOException("视频时长无效");
            }
            Decode.VideoRecord record = readVideoRecord(initSession(false, false), course, job, jobInfo);
            long initialTime = Math.min(duration, record.playedSeconds);
            // A bookmark at the end without a pass is not proof of watched time.
            // Replay with elapsed wall time rather than repeatedly sending an end position.
            if (!record.passed && initialTime >= duration) { initialTime = 0; }
            long playingTime = initialTime;
            long started = System.nanoTime();
            long lastRecordedTime = record.playedSeconds;
            int stalledReports = 0;
            int finalReports = 0;
            boolean firstReport = true;
            int reportInterval = Math.max(10, Math.min(60, jobInfo.getReportTimeInterval() > 0 ? jobInfo.getReportTimeInterval() : 60));
            System.out.println(Thread.currentThread().getName() + "开始学习视频: " + "课程名:" + course.getTitle() + " " + "任务名:" + job.getName());
            log.setCurrentJob(job.getName());
            log.setErrorMessage(null);
            log.setRemark(String.format("学习通播放位置 %d/%d 秒；%s", record.playedSeconds, duration, record.passed ? "平台已确认完成" : "平台尚未确认完成"));
            superStarLogService.updateById(log);
            publishVideoProgress(job, record, duration, playingTime);
            while (!record.passed) {
                taskControl.checkpoint();
                playingTime = Math.min(duration, initialTime + (System.nanoTime() - started) / 1_000_000_000L);
                int eventType = videoEventType(firstReport, playingTime, duration);
                boolean acknowledged = Boolean.TRUE.equals(videoProgressLog(session, course, job, jobInfo, dtoken, duration, playingTime, type, eventType));
                firstReport = false;
                record = readVideoRecord(initSession(false, false), course, job, jobInfo);
                taskControl.checkpoint();
                publishVideoProgress(job, record, duration, playingTime);
                log.setRemark(String.format("学习通播放位置 %d/%d 秒；上报 %d 秒；%s", record.playedSeconds, duration, playingTime,
                        record.passed ? "平台已确认完成" : "平台尚未确认完成"));
                superStarLogService.updateById(log);
                System.out.printf("视频记录核对: 视频=%s 记录=%d/%d秒 平台确认完成=%s%n", job.getName(), record.playedSeconds, duration, record.passed);
                if (record.passed) {
                    break;
                }
                if (record.playedSeconds > lastRecordedTime) {
                    stalledReports = 0;
                } else if (playingTime > lastRecordedTime) {
                    stalledReports++;
                }
                lastRecordedTime = record.playedSeconds;
                if (stalledReports >= 3) {
                    throw new IOException("学习通播放位置连续三次未增长，已停止当前任务");
                }
                if (eventType == 4 && ++finalReports >= 3) {
                    throw new IOException("已到视频末尾，但学习通未确认完成，已停止当前任务");
                }
                if (acknowledged) {
                    log.setRemark(log.getRemark() + "；等待课程记录同步");
                    superStarLogService.updateById(log);
                }
                Thread.sleep(reportInterval * 1000L);
            }
            log.setErrorMessage(null);
            log.setStatus(1);
            log.setRemark("学习通已确认视频完成: " + job.getName());
            log.setMachineNum(serverInfoUtil.getCurrentServerInstance());
            superStarLogService.updateById(log);
        } else {
            throw new IOException("视频信息返回异常状态");
        }
    }

    private void publishVideoProgress(Job job, Decode.VideoRecord record, long duration, long reported) {
        if (localProgress != null) {
            localProgress.video(taskControl.currentTaskId(), job.getName(), record.playedSeconds, duration, record.passed, reported);
        }
    }

    /**
     * 学习文档
     */
    public void studyDocument(Course course, Job job) throws IOException {
        OkHttpClient client = initSession(false, false);
        String otherInfo = job.getOtherInfo();
        Pattern pattern = Pattern.compile("nodeId_(.*?)-");
        Matcher matcher = pattern.matcher(otherInfo);
        String knowledgeId = "";
        if (matcher.find()) {
            knowledgeId = matcher.group(1);
        }
        String url = String.format("https://mooc1.chaoxing.com/ananas/job/document?jobid=%s&knowledgeid=%s&courseid=%s&clazzid=%s&jtoken=%s&_dc=%s",
                job.getJobId(), knowledgeId, course.getCourseId(), course.getClazzId(), job.getJToken(), getTimestamp());
        Request request = new Request.Builder().url(url).build();
        client.newCall(request).execute();
    }

    /**
     * 阅读任务，只完成任务点，不增加时长
     */
    public void studyRead(Course course, Job job, JobInfo jobInfo, SuperStarLog log)
            throws IOException {
        OkHttpClient client = initSession(false, false);
        HttpUrl url = HttpUrl.parse("https://mooc1.chaoxing.com/ananas/job/readv2").newBuilder()
                .addQueryParameter("jobid", job.getJobId())
                .addQueryParameter("knowledgeid", jobInfo.getKnowledgeid())
                .addQueryParameter("jtoken", job.getJToken())
                .addQueryParameter("courseid", course.getCourseId())
                .addQueryParameter("clazzid", course.getClazzId())
                .build();
        Request request = new Request.Builder().url(url).build();
        Response response = client.newCall(request).execute();
        if (response.code() != 200) {
            log.setRemark("阅读任务学习失败 -> [" + response.code() + "]" + response.body().string());
        } else {
            Map<String, Object> respJson = JsonParser.parse(response.body().string());
            log.setRemark("阅读任务学习 -> " + respJson.get("msg"));
        }
        superStarLogService.saveLog(log);
    }

    /**
     * 作业任务
     *
     * @param course  课程信息
     * @param job     任务点信息
     * @param jobInfo 作业信息
     * @throws Exception 异常
     */
    public void studyWork(Course course, Job job, JobInfo jobInfo, SuperStarLog log) throws Exception {

        OkHttpClient client = initSession(false, false);
        String originHtmlContent = "";

        // 获取作业页面
        String url = "https://mooc1.chaoxing.com/mooc-ans/api/work";
        HttpUrl.Builder urlBuilder = HttpUrl.parse(url).newBuilder()
                .addQueryParameter("api", "1")
                .addQueryParameter("workId", job.getJobId().replace("work-", ""))
                .addQueryParameter("jobid", job.getJobId())
                .addQueryParameter("originJobId", job.getJobId())
                .addQueryParameter("needRedirect", "true")
                .addQueryParameter("skipHeader", "true")
                .addQueryParameter("knowledgeid", jobInfo.getKnowledgeid())
                .addQueryParameter("ktoken", jobInfo.getKtoken())
                .addQueryParameter("cpi", jobInfo.getCpi())
                .addQueryParameter("ut", "s")
                .addQueryParameter("clazzId", course.getClazzId())
                .addQueryParameter("enc", job.getEnc())
                .addQueryParameter("mooc2", "1")
                .addQueryParameter("courseid", course.getCourseId());

        Request request = new Request.Builder()
                .url(urlBuilder.build())
                .header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0")
                .get()
                .build();

        try (Response response = client.newCall(request).execute()) {
            if (!response.isSuccessful() || response.body() == null) {
                throw new IOException("章节测验页面读取失败（HTTP " + response.code() + "）");
            }
            originHtmlContent = response.body().string();
        }

        Map<String, Object> questions = Decode.decodeQuestionsInfo(originHtmlContent);

        processAnswers(questions, client, log, job.getTitle());
    }

    /**
     * 切割选项
     *
     * @param options 选项
     * @return 选项信息
     */
    private static List<String> multiCut(String options) {
        if (options == null) return Collections.emptyList();

        return Pattern.compile("\n+")
                .splitAsStream(options)
                .filter(s -> !s.trim().isEmpty())
                .collect(Collectors.toList());
    }

    /**
     * 获取并处理答案
     *
     * @param questions 题目信息
     */
    public void processAnswers(Map<String, Object> questions, OkHttpClient client, SuperStarLog log, String jobTitle) throws IOException {
        List<Question> questionList = workQuestions(questions);
        for (Question q : questionList) {
            q.getAnswerField().setAnswerValue(QuizAnswers.resolve(q, query.queryAnswerSync(q)));
        }

        for (Question q : questionList) {
            questions.put("answer" + q.getId(), q.getAnswerField().getAnswerValue());
            String type = q.getType();
            String reaType = "0";
            switch (type) {
                case "single":
                    reaType = "0";
                    break;
                case "multiple":
                    reaType = "1";
                    break;
                case "fill":
                    reaType = "2";
                    break;
                case "judgement":
                    reaType = "3";
                    break;
                case "short":
                    reaType = "4";
                    break;

            }
            questions.put("answertype" + q.getId(), reaType);
        }
        try {
            questions.remove("questions");
            submitAnswers(questions, client, log, jobTitle);
        } catch (IOException e) {
            throw new RuntimeException(e);
        } catch (NacosException e) {
            throw new RuntimeException(e);
        }
    }


    /**
     * 提交章节测验答案
     *
     * @param questions 题目信息
     * @param client    OkHttpClient
     * @throws IOException 异常
     */
    private void submitAnswers(Map<String, Object> questions, OkHttpClient client, SuperStarLog log, String jobTitle) throws IOException, NacosException {
        FormBody.Builder formBodyBuilder = new FormBody.Builder(StandardCharsets.UTF_8);

        for (Map.Entry<String, Object> entry : questions.entrySet()) {
            formBodyBuilder.add(entry.getKey(), entry.getValue().toString());
        }
        Request request = new Request.Builder()
                .url("https://mooc1.chaoxing.com/mooc-ans/work/addStudentWorkNewWeb")  //2025/4/26发现学习通提交题目接口已改变
                .post(formBodyBuilder.build())
                .header("Host", "mooc1.chaoxing.com")
                .header("sec-ch-ua-platform", "\"Windows\"")
                .header("X-Requested-With", "XMLHttpRequest")
                .header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0")
                .header("Accept", "application/json, text/javascript, */*; q=0.01")
                .header("sec-ch-ua", "\"Microsoft Edge\";v=\"129\", \"Not=A?Brand\";v=\"8\", \"Chromium\";v=\"129\"")
                .header("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8")
                .header("sec-ch-ua-mobile", "?0")
                .header("Origin", "https://mooc1.chaoxing.com")
                .header("Sec-Fetch-Site", "same-origin")
                .header("Sec-Fetch-Mode", "cors")
                .header("Sec-Fetch-Dest", "empty")
                .header("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6,ja;q=0.5")
                .build();

        try (Response response = client.newCall(request).execute()) {
            if (!response.isSuccessful() || response.body() == null) {
                throw new IOException("章节测验提交失败（HTTP " + response.code() + "）");
            }
            Map<String, Object> responseBody = new ObjectMapper().readValue(response.body().string(), Map.class);
            requireQuizSubmissionSuccess(responseBody);
        }
        log.setErrorMessage(null);
        log.setStatus(1);
        log.setRemark("章节测验提交成功: " + jobTitle);
        log.setMachineNum(serverInfoUtil.getCurrentServerInstance());
        superStarLogService.updateById(log);
    }

    public static List<Question> workQuestions(Map<String, Object> form) throws IOException {
        Object raw = form.get("questions");
        if (!(raw instanceof List) || ((List<?>) raw).isEmpty()) {
            throw new IOException("未解析到章节测验题目；测验未提交");
        }
        List<Question> result = new ArrayList<>();
        for (Object value : (List<?>) raw) {
            if (!(value instanceof Map)) { throw new IOException("测验题目格式不兼容；测验未提交"); }
            Map<String, Object> item = (Map<String, Object>) value;
            AnswerField<String> fields = new AnswerField<>();
            Object rawFields = item.get("answerField");
            if (!(rawFields instanceof Map)) { throw new IOException("测验答案字段缺失；测验未提交"); }
            fields.updateFromMap((Map<String, Object>) rawFields);
            Question question = new Question((String) item.get("id"), QuizAnswers.title((String) item.get("title")),
                    (String) item.get("options"), (String) item.get("type"), fields);
            if (question.getTitle().isEmpty() || question.getType() == null) {
                throw new IOException("测验题干或题型缺失；测验未提交");
            }
            question.setTKOptions(multiCut(question.getOptions()));
            result.add(question);
        }
        return result;
    }

    static void requireQuizSubmissionSuccess(Map<String, Object> response) throws IOException {
        if (response == null || !"true".equalsIgnoreCase(String.valueOf(response.get("status")))
                || !"success".equals(response.get("msg"))) {
            throw new IOException("学习通未确认章节测验提交成功，任务已停止");
        }
    }

}

