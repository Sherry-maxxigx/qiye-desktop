package com.tihai.controller;

import org.springframework.context.annotation.Profile;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

import java.util.LinkedHashMap;
import java.util.Map;

/** Read-only readiness check for the local installation. */
@Profile("local")
@RestController
public class LocalHealthController {
    private final JdbcTemplate jdbcTemplate;
    @Value("${qiye.installation-id:legacy}")
    private String installationId;

    public LocalHealthController(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        Integer ready = jdbcTemplate.queryForObject("SELECT 1", Integer.class);
        if (!Integer.valueOf(1).equals(ready)) {
            throw new IllegalStateException("Database check failed");
        }
        Map<String, String> result = new LinkedHashMap<>();
        result.put("status", "UP");
        result.put("database", "UP");
        result.put("installationId", installationId);
        return result;
    }
}
