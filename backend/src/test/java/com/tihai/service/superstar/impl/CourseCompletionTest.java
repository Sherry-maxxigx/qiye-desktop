package com.tihai.service.superstar.impl;

import com.tihai.common.*;
import com.tihai.domain.chaoxing.SuperStarLog;
import com.tihai.domain.chaoxing.SuperStarTask;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.mapper.SuperStarMapper;
import com.tihai.manager.StudyTaskControl;
import com.tihai.service.superstar.SuperStarLogService;
import com.tihai.properties.StudyProperties;
import com.tihai.utils.CourseUtil;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.test.util.ReflectionTestUtils;
import java.io.IOException;
import java.math.BigDecimal;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;

class CourseCompletionTest {
    private SuperStarTaskServiceImpl service;
    private CourseUtil courseUtil;
    private SuperStarTask task;
    private SuperStarLog log;
    private Course course;

    @BeforeEach
    void setup() throws Exception {
        service = new SuperStarTaskServiceImpl();
        courseUtil = mock(CourseUtil.class);
        ReflectionTestUtils.setField(service, "taskControl", new StudyTaskControl());
        ReflectionTestUtils.setField(service, "superStarLogService", mock(SuperStarLogService.class));
        ReflectionTestUtils.setField(service, "courseUtil", courseUtil);
        ReflectionTestUtils.setField(service, "baseMapper", mock(SuperStarMapper.class));
        StudyProperties properties = new StudyProperties();
        properties.setSpeed(1);
        ReflectionTestUtils.setField(service, "studyProperties", properties);
        task = new SuperStarTask();
        task.setId("test");
        task.setStatus(1);
        log = new SuperStarLog();
        log.setId(1L);
        log.setStatus(1);
        log.setCurrentProgress(BigDecimal.ZERO);
        course = new Course();
        course.setClazzId("class");
        course.setCourseId("course");
        course.setCpi("cpi");
    }

    private List<CoursePoint> chapters() {
        List<ChapterPoint> chapters = new ArrayList<>();
        for (int i = 0; i < 3; i++) {
            ChapterPoint chapter = new ChapterPoint();
            chapter.setId("chapter" + i);
            chapters.add(chapter);
        }
        CoursePoint root = new CoursePoint();
        root.setPoints(chapters);
        return Collections.singletonList(root);
    }

    private void process(List<CoursePoint> points) {
        ReflectionTestUtils.invokeMethod(service, "processCoursePoints", task, log, course, points);
    }

    @Test
    void failureAfterFirstChapterCannotMarkCourseFinished() throws Exception {
        log.setCurrentChapterIndex(1);
        Job job = new Job();
        job.setType("video");
        when(courseUtil.getJobList(anyString(), anyString(), anyString(), anyString()))
                .thenReturn(new Pair<>(Collections.singletonList(job), Collections.singletonList(new JobInfo())));
        doThrow(new IOException("platform record unchanged")).when(courseUtil)
                .studyVideo(any(), any(), any(), anyDouble(), anyString(), any());
        process(chapters());
        assertEquals(5, task.getStatus());
        assertEquals(5, log.getStatus());
        assertEquals(1, log.getCurrentChapterIndex());
        assertTrue(log.getCurrentProgress().compareTo(BigDecimal.valueOf(100)) < 0);
        assertTrue(log.getErrorMessage().contains("platform record unchanged"));
        verify(courseUtil, times(1)).getJobList(anyString(), anyString(), anyString(), anyString());
    }

    @Test
    void allChaptersMustBeProcessedBeforeCompletion() throws Exception {
        when(courseUtil.getJobList(anyString(), anyString(), anyString(), anyString()))
                .thenReturn(new Pair<>(Collections.emptyList(), Collections.emptyList()));
        process(chapters());
        verify(courseUtil, times(3)).getJobList(anyString(), anyString(), anyString(), anyString());
        assertEquals(4, task.getStatus());
        assertEquals(4, log.getStatus());
        assertEquals(3, log.getCurrentChapterIndex());
        assertEquals(0, BigDecimal.valueOf(100).compareTo(log.getCurrentProgress()));
    }

    @Test
    void lockedChapterPausesInsteadOfCompleting() throws Exception {
        JobInfo locked = new JobInfo();
        locked.setNotOpen(true);
        when(courseUtil.getJobList(anyString(), anyString(), anyString(), anyString()))
                .thenReturn(new Pair<>(Collections.emptyList(), Collections.singletonList(locked)));
        process(chapters());
        assertEquals(6, task.getStatus());
        assertEquals(6, log.getStatus());
        assertEquals(0, log.getCurrentChapterIndex());
    }

    @Test
    void missingChaptersAreAnError() {
        process(Collections.emptyList());
        assertEquals(5, task.getStatus());
        assertEquals(5, log.getStatus());
    }
}
