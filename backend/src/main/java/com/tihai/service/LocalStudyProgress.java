package com.tihai.service;

import org.springframework.context.annotation.Profile;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Component;
import javax.annotation.PostConstruct;

/** Persist display metadata separately from legacy chapter percentages. */
@Component
@Profile("local")
public class LocalStudyProgress {
    private final JdbcTemplate jdbc;
    public LocalStudyProgress(JdbcTemplate jdbc) { this.jdbc = jdbc; }

    @PostConstruct
    public void initialize() {
        jdbc.queryForList("SELECT task_id FROM th_local_progress LIMIT 0");
    }

    public void chapter(String id, String title, int total) {
        if (id == null) { return; }
        jdbc.update("INSERT INTO th_local_progress(task_id,chapter_title,total_chapters,updated_at) VALUES(?,?,?,NOW(3)) "
                + "ON DUPLICATE KEY UPDATE chapter_title=VALUES(chapter_title),total_chapters=VALUES(total_chapters),updated_at=NOW(3)",
                id, title, total);
    }

    public void video(String id, String name, long position, long duration, boolean passed, long reported) {
        if (id == null) { return; }
        jdbc.update("INSERT INTO th_local_progress(task_id,video_name,video_position,video_duration,platform_passed,reported_position,updated_at) "
                + "VALUES(?,?,?,?,?,?,NOW(3)) ON DUPLICATE KEY UPDATE video_name=VALUES(video_name),video_position=VALUES(video_position),"
                + "video_duration=VALUES(video_duration),platform_passed=VALUES(platform_passed),reported_position=VALUES(reported_position),updated_at=NOW(3)",
                id, name, position, duration, passed, reported);
    }
}
