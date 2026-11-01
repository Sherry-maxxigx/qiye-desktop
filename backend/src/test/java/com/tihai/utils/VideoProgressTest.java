package com.tihai.utils;

import org.junit.jupiter.api.Test;
import java.io.IOException;
import static org.junit.jupiter.api.Assertions.*;

class VideoProgressTest {
    @Test
    void httpSuccessDoesNotMeanVideoPassed() throws Exception {
        assertFalse(CourseUtil.parseVideoProgressResponse(200, "{\"isPassed\":false}"));
        assertTrue(CourseUtil.parseVideoProgressResponse(200, "{\"isPassed\":true}"));
    }

    @Test
    void failuresAndMissingConfirmationAreRejected() {
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(403, "{}"));
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(200, null));
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(200, "{}"));
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(200, "{\"isPassed\":\"true\"}"));
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(200, "{\"isPassed\":true,\"videoTimeLimit\":true}"));
        assertThrows(IOException.class, () -> CourseUtil.parseVideoProgressResponse(200, "{\"isPassed\":true,\"hasJobLimit\":true}"));
    }

    @Test
    void platformRecordUsesMatchingJobAndMilliseconds() {
        String html = "<script>var mArg = {\n\"attachments\":["
                + "{\"jobid\":\"other\",\"playTime\":900000,\"isPassed\":true},"
                + "{\"jobid\":\"video\",\"playTime\":111000,\"isPassed\":false}]};</script>";
        Decode.VideoRecord record = Decode.decodeVideoRecord(html, "video");
        assertNotNull(record);
        assertEquals(111, record.playedSeconds);
        assertFalse(record.passed);
        assertNull(Decode.decodeVideoRecord(html, "missing"));
        assertNull(Decode.decodeVideoRecord("<html>login</html>", "video"));
    }

    @Test
    void headOffsetAndActualPassStateAreSupported() {
        Decode.VideoRecord record = Decode.decodeVideoRecord(
                "mArg={\"attachments\":[{\"jobid\":\"video\",\"headOffset\":1203000,\"isPassed\":true}]};", "video");
        assertEquals(1203, record.playedSeconds);
        assertTrue(record.passed);
    }
}
