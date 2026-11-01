package com.tihai.controller;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.domain.chaoxing.SuperStarTask;
import com.tihai.domain.chaoxing.WkUser;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.manager.StudyTaskControl;
import com.tihai.service.LocalStudyProgress;
import com.tihai.service.LocalBrowserSession;
import com.tihai.service.superstar.SuperStarLoginService;
import com.tihai.service.superstar.impl.SuperStarTaskServiceImpl;
import com.tihai.utils.CourseUtil;
import org.springframework.context.annotation.Profile;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.server.ResponseStatusException;
import javax.annotation.PostConstruct;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.regex.*;

/** Local desktop API. No passwords, cookies, or legacy task entities are returned. */
@Profile("local")
@RestController
@RequestMapping("/local/desktop")
public class LocalDesktopController {
    private final JdbcTemplate jdbc;
    private final SuperStarTaskServiceImpl tasks;
    private final StudyTaskControl control;
    private final CourseUtil courses;
    private final SuperStarLoginService login;
    private final ObjectMapper json;
    @org.springframework.beans.factory.annotation.Autowired
    private LocalBrowserSession browserSession;
    private final Path courseFile = Paths.get("data", "course-list.json");
    private final Path accountFile = Paths.get("data", "desktop-account.txt");

    public LocalDesktopController(JdbcTemplate jdbc, SuperStarTaskServiceImpl tasks, StudyTaskControl control,
                                  CourseUtil courses, SuperStarLoginService login, ObjectMapper json,
                                  LocalStudyProgress progress) {
        this.jdbc = jdbc; this.tasks = tasks; this.control = control;
        this.courses = courses; this.login = login; this.json = json;
    }

    @PostConstruct
    public void recoverInterruptedTasks() {
        jdbc.update("UPDATE th_wk_log l JOIN th_wk_queue q ON q.login_account=l.login_account AND q.course_name=l.course_name "
                + "SET l.status=6 WHERE q.status IN (0,1,2,3) AND l.status IN (0,1,2,3)");
        jdbc.update("UPDATE th_wk_queue SET status=6 WHERE status IN (0,1,2,3)");
    }

    @GetMapping("/state")
    public Map<String, Object> state() throws Exception {
        Map<String, Object> result = new LinkedHashMap<>();
        List<Map<String, Object>> users = savedUsers();
        String account = users.isEmpty() ? "" : string(users.get(0).get("account"));
        result.put("accountHint", account.length() > 6 ? account.substring(0,3) + "****" + account.substring(account.length()-4) : account);
        result.put("courses", readCachedCourses());
        List<Map<String, Object>> rows = jdbc.queryForList("SELECT q.id AS taskId,q.course_id AS courseId,q.course_name AS title,q.status,"
                + "l.current_chapter_index AS chapterIndex,l.current_progress AS chapterPercent,l.current_job AS currentVideo,"
                + "l.remark AS detail,l.error_message AS message,p.chapter_title AS chapterTitle,p.total_chapters AS totalChapters,"
                + "p.video_name AS videoName,p.video_position AS videoPosition,p.video_duration AS videoDuration,"
                + "p.platform_passed AS platformPassed,p.reported_position AS reportedPosition,p.updated_at AS updatedAt "
                + "FROM th_wk_queue q LEFT JOIN th_wk_log l ON l.id=(SELECT MAX(l2.id) FROM th_wk_log l2 "
                + "WHERE l2.login_account=q.login_account AND l2.course_name=q.course_name) "
                + "LEFT JOIN th_local_progress p ON p.task_id=q.id WHERE q.login_account=? ORDER BY q.creat_time DESC", account);
        for (Map<String, Object> row : rows) {
            String id = string(row.get("taskId"));
            row.put("active", control.isActive(id));
            row.put("pauseRequested", control.isPauseRequested(id));
            if (row.get("updatedAt") != null) { row.put("updatedAt", row.get("updatedAt").toString()); }
            if (row.get("videoDuration") == null) {
                Matcher match = Pattern.compile("(?:位置|记录) (\\d+)/(\\d+) 秒").matcher(string(row.get("detail")));
                if (match.find()) {
                    row.put("videoPosition", Long.parseLong(match.group(1)));
                    row.put("videoDuration", Long.parseLong(match.group(2)));
                    row.put("platformPassed", !string(row.get("detail")).contains("尚未确认")
                            && string(row.get("detail")).contains("已确认"));
                }
            }
        }
        result.put("tasks", rows);
        result.put("serverTime", LocalDateTime.now().toString());
        return result;
    }

    @PostMapping(value="/courses", consumes="application/json")
    public synchronized Map<String, Object> refreshCourses(@RequestBody Map<String, String> body) throws Exception {
        if (control.hasActiveExecutions()) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "请先暂停正在执行的任务，再刷新登录和课程");
        }
        WkUser user = new WkUser();
        String account = body.get("account");
        if (account == null || account.trim().isEmpty()) {
            List<Map<String, Object>> users = savedUsers();
            if (users.isEmpty()) { throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "请先输入账号和密码登录"); }
            Map<String, Object> saved = users.get(0);
            user.setAccount(string(saved.get("account")));
            user.setPassword(string(saved.get("password")));
            user.setCookies(string(saved.get("cookies")));
        } else {
            user.setAccount(account.trim());
            user.setPassword(body.get("password"));
            if (user.getPassword() == null || user.getPassword().isEmpty()) {
                throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "请输入密码");
            }
        }
        String cookies = login.login(user, user.getCookies() == null || user.getCookies().isEmpty());
        if (cookies == null) { throw new ResponseStatusException(HttpStatus.UNAUTHORIZED, "登录失败，请检查账号密码或在学习通处理登录验证"); }
        courses.setAccount(user.getAccount()); courses.setCookies(cookies);
        List<Course> list = courses.getCourseList();
        if (list == null || list.isEmpty()) {
            throw new ResponseStatusException(HttpStatus.BAD_GATEWAY, "没有读取到课程，请重新登录后再试");
        }
        Files.createDirectories(courseFile.getParent());
        Path temporary = courseFile.resolveSibling("course-list.desktop.tmp");
        json.writeValue(temporary.toFile(), list);
        Files.move(temporary, courseFile, StandardCopyOption.REPLACE_EXISTING);
        Files.write(accountFile, user.getAccount().getBytes(StandardCharsets.UTF_8));
        return state();
    }

    @PostMapping(value="/browser-login", consumes="application/json")
    public synchronized Map<String,Object> browserLogin(@RequestBody Map<String,String> body) throws Exception {
        if (control.hasActiveExecutions()) {
            throw new ResponseStatusException(HttpStatus.CONFLICT,"请先暂停正在执行的任务，再扫码切换账号");
        }
        final LocalBrowserSession.Session session;
        try { session=browserSession.verify(body.get("passportCookies"),body.get("courseCookies")); }
        catch (java.io.IOException failure) { throw new ResponseStatusException(HttpStatus.BAD_REQUEST,failure.getMessage()); }
        String account="";
        // Reuse the same local account/history only when its saved official UID matches.
        for (Map<String,Object> saved : jdbc.queryForList("SELECT account,cookies FROM th_wk_user ORDER BY id")) {
            String oldUid=CourseUtil.convertCookieStringToMap(string(saved.get("cookies"))).get("_uid");
            if (session.uid.equals(oldUid)) { account=string(saved.get("account"));break; }
        }
        if (account.isEmpty()) { account=session.phone.matches("\\d{5,20}") ? session.phone : session.uid; }
        List<Map<String,Object>> existing=jdbc.queryForList("SELECT id FROM th_wk_user WHERE account=? LIMIT 1",account);
        if (existing.isEmpty()) {
            jdbc.update("INSERT INTO th_wk_user(account,password,name,cookies) VALUES(?,NULL,?,?)",account,session.name,session.storedCookies);
        } else {
            jdbc.update("UPDATE th_wk_user SET cookies=?,name=? WHERE id=?",session.storedCookies,session.name,existing.get(0).get("id"));
        }
        Files.createDirectories(courseFile.getParent());
        Path temporary=courseFile.resolveSibling("course-list.desktop.tmp");
        json.writeValue(temporary.toFile(),session.courses);
        Files.move(temporary,courseFile,StandardCopyOption.REPLACE_EXISTING);
        Files.write(accountFile,account.getBytes(StandardCharsets.UTF_8));
        return state();
    }

    @PostMapping(value="/start", consumes="application/json")
    public synchronized Map<String, Object> start(@RequestBody Map<String, String> body) throws Exception {
        String courseId = body.get("courseId");
        Map<String, Object> selected = readCachedCourses().stream()
                .filter(course -> Objects.equals(string(course.get("courseId")), courseId))
                .findFirst().orElseThrow(() -> new ResponseStatusException(HttpStatus.BAD_REQUEST, "请先选择课程"));
        List<Map<String, Object>> users = savedUsers();
        if (users.isEmpty()) { throw new ResponseStatusException(HttpStatus.UNAUTHORIZED, "请先登录"); }
        String account = string(users.get(0).get("account"));
        SuperStarTask task = tasks.getSuperStarTask(account, courseId);
        if (task == null) {
            task = new SuperStarTask();
            task.setId(LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMddHHmmssSSS")));
            task.setLoginAccount(account); task.setPassword(string(users.get(0).get("password")));
            task.setCourseId(courseId); task.setCourseName(string(selected.get("title")));
            task.setStatus(6); task.setPriority(1); task.setRetryCount(0); task.setCreatTime(LocalDateTime.now());
            tasks.save(task);
        }
        try {
            if (!control.isActive(task.getId())) {
                jdbc.update("UPDATE th_wk_log SET error_message=NULL,end_time=NULL WHERE id=(SELECT latest.id FROM "
                        + "(SELECT MAX(id) AS id FROM th_wk_log WHERE login_account=? AND course_name=?) latest)",
                        account, task.getCourseName());
            }
            tasks.resumeTask(task.getId());
        } catch (IllegalStateException failure) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, failure.getMessage());
        }
        return state();
    }

    @PostMapping(value="/pause", consumes="application/json")
    public Map<String, Object> pause(@RequestBody Map<String, String> body) throws Exception {
        String id = body.get("taskId");
        try { tasks.pauseTask(id == null ? "" : id); }
        catch (IllegalArgumentException failure) { throw new ResponseStatusException(HttpStatus.NOT_FOUND, failure.getMessage()); }
        return state();
    }

    private List<Map<String, Object>> savedUsers() throws Exception {
        if (Files.exists(accountFile)) {
            String account = new String(Files.readAllBytes(accountFile), StandardCharsets.UTF_8).trim();
            return jdbc.queryForList("SELECT account,password,cookies FROM th_wk_user WHERE account=? LIMIT 1", account);
        }
        return jdbc.queryForList("SELECT account,password,cookies FROM th_wk_user ORDER BY id LIMIT 1");
    }

    private List<Map<String, Object>> readCachedCourses() throws Exception {
        if (!Files.exists(courseFile)) { return Collections.emptyList(); }
        String text = new String(Files.readAllBytes(courseFile), StandardCharsets.UTF_8).replace("\uFEFF", "");
        return json.readValue(text, new TypeReference<List<Map<String, Object>>>() {});
    }
    private static String string(Object value) { return value == null ? "" : value.toString(); }

    @ExceptionHandler(ResponseStatusException.class)
    public ResponseEntity<Map<String, String>> requestError(ResponseStatusException failure) {
        return ResponseEntity.status(failure.getStatus()).body(Collections.singletonMap("message", failure.getReason()));
    }
}
