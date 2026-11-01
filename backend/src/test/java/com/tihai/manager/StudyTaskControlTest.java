package com.tihai.manager;

import org.junit.jupiter.api.Test;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import static org.junit.jupiter.api.Assertions.*;

class StudyTaskControlTest {
    private ThreadPoolExecutor executor() {
        return new ThreadPoolExecutor(1, 1, 30, TimeUnit.SECONDS, new PriorityBlockingQueue<>());
    }

    @Test
    void pauseInterruptsTheRunningWaitAndAllowsCleanResume() throws Exception {
        ThreadPoolExecutor pool = executor();
        try {
            StudyTaskControl control = new StudyTaskControl();
            CountDownLatch started = new CountDownLatch(1);
            CountDownLatch paused = new CountDownLatch(1);
            AtomicBoolean advanced = new AtomicBoolean();
            assertTrue(control.submit("one", 1, pool, () -> {
                started.countDown();
                try { Thread.sleep(60000); }
                catch (InterruptedException expected) { control.checkpoint(); }
                advanced.set(true);
            }, paused::countDown));
            assertTrue(started.await(3, TimeUnit.SECONDS));
            assertFalse(control.submit("one", 1, pool, () -> fail("duplicate execution"), () -> {}));
            assertTrue(control.pause("one", pool));
            assertTrue(paused.await(3, TimeUnit.SECONDS));
            pool.shutdown();
            assertTrue(pool.awaitTermination(3, TimeUnit.SECONDS));
            assertFalse(advanced.get());
            assertFalse(control.isActive("one"));

            ThreadPoolExecutor resumed = executor();
            try {
                CountDownLatch ran = new CountDownLatch(1);
                AtomicBoolean inheritedInterrupt = new AtomicBoolean(true);
                assertTrue(control.submit("one", 1, resumed, () -> {
                    inheritedInterrupt.set(Thread.currentThread().isInterrupted());
                    assertEquals("one", control.currentTaskId());
                    ran.countDown();
                }, () -> fail("unexpected pause")));
                assertTrue(ran.await(3, TimeUnit.SECONDS));
                assertFalse(inheritedInterrupt.get());
            } finally { resumed.shutdownNow(); }
        } finally { pool.shutdownNow(); }
    }

    @Test
    void pausingQueuedTaskRemovesItWithoutRunningItsAction() throws Exception {
        ThreadPoolExecutor pool = executor();
        CountDownLatch blocker = new CountDownLatch(1);
        try {
            StudyTaskControl control = new StudyTaskControl();
            CountDownLatch started = new CountDownLatch(1);
            control.submit("running", 1, pool, () -> {
                started.countDown();
                try { blocker.await(); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); }
            }, () -> {});
            assertTrue(started.await(3, TimeUnit.SECONDS));
            AtomicBoolean actionRan = new AtomicBoolean();
            AtomicInteger pauseCallbacks = new AtomicInteger();
            control.submit("queued", 1, pool, () -> actionRan.set(true), pauseCallbacks::incrementAndGet);
            assertTrue(control.pause("queued", pool));
            assertFalse(control.isActive("queued"));
            assertEquals(1, pauseCallbacks.get());
            assertFalse(actionRan.get());
            assertTrue(pool.getQueue().isEmpty());
            assertFalse(control.pause("absent", pool));
        } finally { blocker.countDown(); pool.shutdownNow(); }
    }
}
