package com.tihai.utils;

import com.tihai.common.Job;
import com.tihai.dubbo.pojo.course.Course;
import okhttp3.HttpUrl;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class VideoPlaybackCompatibilityTest {
    @Test
    void playbackStartHeartbeatAndEndAreDifferentEvents() {
        assertEquals(3, CourseUtil.videoEventType(true, 20, 1203));
        assertEquals(0, CourseUtil.videoEventType(false, 80, 1203));
        assertEquals(4, CourseUtil.videoEventType(false, 1203, 1203));
    }

    @Test
    void signedAttachmentMetadataSurvivesDecodeAndUrlEncoding() throws Exception {
        String html = "mArg={\"defaults\":{},\"attachments\":[{\"job\":true,\"type\":\"video\",\"jobid\":\"job\","
                + "\"mid\":\"mid\",\"objectId\":\"object\",\"otherInfo\":\"nodeId_chapter&courseId=course\","
                + "\"property\":{\"name\":\"video.mp4\"},\"attDuration\":1203,"
                + "\"attDurationEnc\":\"duration+signature/=\",\"videoFaceCaptureEnc\":\"capture+signature/=\"}]};";
        Job job = Decode.decodeCourseCard(html).getFirst().get(0);
        Course course = new Course();
        course.setClazzId("class"); course.setCourseId("course"); course.setCpi("cpi");
        HttpUrl url = CourseUtil.createVideoProgressUrl(course, job, "token", 1203, 80, "Video", "user", 0);
        assertEquals("duration+signature/=", url.queryParameter("attDurationEnc"));
        assertEquals("capture+signature/=", url.queryParameter("videoFaceCaptureEnc"));
        assertEquals("1203", url.queryParameter("attDuration"));
        assertEquals("course", url.queryParameter("courseId"));
        assertEquals(1, url.queryParameterValues("courseId").size());
        assertEquals("nodeId_chapter", url.queryParameter("otherInfo"));
        assertEquals("0", url.queryParameter("isdrag"));
        assertEquals("1", url.queryParameter("rt"));
        assertEquals("80", url.queryParameter("playingTime"));
    }
}
