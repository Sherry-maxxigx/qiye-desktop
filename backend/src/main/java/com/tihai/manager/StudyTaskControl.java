package com.tihai.manager;

import com.tihai.queue.PriorityTaskWrapper;
import org.springframework.stereotype.Component;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ThreadPoolExecutor;

/** Owns each queued/running execution so pause can interrupt waits without ending the pool. */
@Component
public class StudyTaskControl {
    private final ConcurrentHashMap<String, Execution> executions = new ConcurrentHashMap<>();
    private final ThreadLocal<Execution> current = new ThreadLocal<>();

    public static class PausedException extends RuntimeException {
        public PausedException() { super("任务已暂停"); }
    }

    private static class Execution {
        String id;
        volatile boolean pauseRequested;
        boolean finished;
        Thread thread;
        PriorityTaskWrapper wrapper;
        Runnable onPause;
    }

    public boolean submit(String id, int priority, ThreadPoolExecutor executor, Runnable action, Runnable onPause) {
        Execution execution = new Execution();
        execution.id = id;
        execution.onPause = onPause;
        execution.wrapper = new PriorityTaskWrapper(() -> {
            synchronized (execution) { execution.thread = Thread.currentThread(); }
            current.set(execution);
            try {
                checkpoint();
                action.run();
                checkpoint();
            } catch (RuntimeException failure) {
                if (!execution.pauseRequested) { throw failure; }
            } finally {
                current.remove();
                // Clear our pause interrupt before persisting state or reusing the pool thread.
                if (execution.pauseRequested) { Thread.interrupted(); }
                try {
                    synchronized (execution) {
                        execution.finished = true;
                        if (execution.pauseRequested) { execution.onPause.run(); }
                    }
                } finally {
                    executions.remove(id, execution);
                }
            }
        }, priority, id);
        if (executions.putIfAbsent(id, execution) != null) { return false; }
        try {
            executor.execute(execution.wrapper);
            return true;
        } catch (RuntimeException failure) {
            executions.remove(id, execution);
            throw failure;
        }
    }

    public boolean pause(String id, ThreadPoolExecutor executor) {
        Execution execution = executions.get(id);
        if (execution == null) { return false; }
        synchronized (execution) {
            if (execution.finished) { return false; }
            execution.pauseRequested = true;
            if (execution.thread != null) {
                execution.thread.interrupt();
            } else if (executor.remove(execution.wrapper)) {
                execution.finished = true;
                try { execution.onPause.run(); }
                finally { executions.remove(id, execution); }
            }
        }
        return true;
    }

    public boolean isActive(String id) { return executions.containsKey(id); }
    public boolean hasActiveExecutions() { return !executions.isEmpty(); }
    public boolean isPauseRequested(String id) {
        Execution execution = executions.get(id);
        return execution != null && execution.pauseRequested;
    }
    public String currentTaskId() {
        Execution execution = current.get();
        return execution == null ? null : execution.id;
    }
    public void checkpoint() {
        Execution execution = current.get();
        if (execution != null && (execution.pauseRequested || Thread.currentThread().isInterrupted())) {
            throw new PausedException();
        }
    }
}
