package com.tihai.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.tihai.controller.LocalDesktopController;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.manager.StudyTaskControl;
import com.tihai.service.superstar.SuperStarLoginService;
import com.tihai.service.superstar.impl.SuperStarTaskServiceImpl;
import com.tihai.utils.CourseUtil;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.server.ResponseStatusException;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.mockito.ArgumentMatchers.*;

class LocalBrowserLoginControllerTest {
    @TempDir Path directory;
    private final JdbcTemplate jdbc=mock(JdbcTemplate.class);
    private final StudyTaskControl control=mock(StudyTaskControl.class);
    private final LocalBrowserSession session=mock(LocalBrowserSession.class);
    private LocalDesktopController controller() {
        LocalDesktopController controller=new LocalDesktopController(jdbc,mock(SuperStarTaskServiceImpl.class),control,
                mock(CourseUtil.class),mock(SuperStarLoginService.class),new ObjectMapper(),mock(LocalStudyProgress.class));
        ReflectionTestUtils.setField(controller,"browserSession",session);
        ReflectionTestUtils.setField(controller,"courseFile",directory.resolve("course-list.json"));
        ReflectionTestUtils.setField(controller,"accountFile",directory.resolve("desktop-account.txt"));
        return controller;
    }
    @Test void unverifiedSessionDoesNotChangeSavedAccountOrCourses() throws Exception {
        when(session.verify(any(),any())).thenThrow(new IOException("官方尚未确认登录"));
        assertThrows(ResponseStatusException.class,()->controller().browserLogin(Collections.emptyMap()));
        verifyNoInteractions(jdbc);assertFalse(Files.exists(directory.resolve("course-list.json")));
    }
    @Test void activeLegacyTaskPreventsAccountSwitch() {
        when(control.hasActiveExecutions()).thenReturn(true);
        assertThrows(ResponseStatusException.class,()->controller().browserLogin(Collections.emptyMap()));
        verifyNoInteractions(session,jdbc);
    }
    @Test void matchingOfficialUidRetainsAccountAndHistoryWithoutReturningCookies() throws Exception {
        Map<String,Object> saved=new LinkedHashMap<>();saved.put("account","13800000000");saved.put("id",1L);saved.put("cookies","_uid=123456789; path=/");
        when(jdbc.queryForList(anyString())).thenReturn(Collections.singletonList(saved));
        when(jdbc.queryForList(contains("WHERE account=? LIMIT 1"),any(Object.class))).thenReturn(Collections.singletonList(saved));
        LocalBrowserSession.Session verified=new LocalBrowserSession.Session();verified.uid="123456789";verified.phone="13900000000";
        verified.name="测试";verified.storedCookies="_uid=123456789; _d=PRIVATE_SESSION";
        Course course=new Course();course.setCourseId("101");course.setTitle("扫码课程");verified.courses=Collections.singletonList(course);
        when(session.verify(any(),any())).thenReturn(verified);
        Map<String,Object> result=controller().browserLogin(Collections.emptyMap());
        assertEquals("13800000000",new String(Files.readAllBytes(directory.resolve("desktop-account.txt")),"UTF-8"));
        String response=new ObjectMapper().writeValueAsString(result);assertTrue(response.contains("扫码课程"));
        assertFalse(response.contains("PRIVATE_SESSION"));assertFalse(response.contains("storedCookies"));
        verify(jdbc).update(eq("UPDATE th_wk_user SET cookies=?,name=? WHERE id=?"),eq(verified.storedCookies),eq("测试"),eq(1L));
    }
}
