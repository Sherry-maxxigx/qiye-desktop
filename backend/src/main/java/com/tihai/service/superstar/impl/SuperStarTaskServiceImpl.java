package com.tihai.service.superstar.impl;

import com.alibaba.nacos.api.exception.NacosException;
import com.baomidou.mybatisplus.core.conditions.query.LambdaQueryWrapper;
import com.baomidou.mybatisplus.extension.service.impl.ServiceImpl;
import com.tihai.common.*;
import com.tihai.constant.GlobalConstant;
import com.tihai.constant.JobTypeConstant;
import com.tihai.constant.RetryConstant;
import com.tihai.domain.chaoxing.SuperStarLog;
import com.tihai.domain.chaoxing.SuperStarTask;
import com.tihai.domain.chaoxing.WkUser;
import com.tihai.dubbo.dto.CourseSubmitTaskDTO;
import com.tihai.dubbo.pojo.course.Course;
import com.tihai.enums.BizCodeEnum;
import com.tihai.enums.WkTaskStatusEnum;
import com.tihai.exception.BusinessException;
import com.tihai.factory.CustomThreadFactory;
import com.tihai.factory.PriorityRejectPolicy;
import com.tihai.manager.RollBackManager;
import com.tihai.manager.StudyTaskControl;
import com.tihai.service.LocalStudyProgress;
import com.tihai.mapper.SuperStarMapper;
import com.tihai.properties.StudyProperties;
import com.tihai.properties.ThreadPoolProperties;
import com.tihai.queue.PriorityTaskWrapper;
import com.tihai.service.superstar.SuperStarLogService;
import com.tihai.service.superstar.SuperStarLoginService;
import com.tihai.service.superstar.SuperStarTaskService;
import com.tihai.service.superstar.SuperStarUserService;
import com.tihai.utils.CourseUtil;
import com.tihai.utils.ServerInfoUtil;
import lombok.extern.slf4j.Slf4j;
import ma.glasnost.orika.MapperFacade;
import org.apache.commons.lang3.StringUtils;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import javax.annotation.PostConstruct;
import java.math.BigDecimal;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;


/**
 * @Copyright : DuanInnovator
 * @Description : 超星学习通-任务启动和实现
 * @Author : DuanInnovator
 * @CreateTime : 2025/4/26
 * @Link : <a href="https://github.com/DuanInnovator/TiHaiWuYou-Admin/tree/mine-admin">...</a>
 **/
@Service
@Slf4j
public class SuperStarTaskServiceImpl extends ServiceImpl<SuperStarMapper, SuperStarTask> implements SuperStarTaskService {

    @Autowired
    private MapperFacade mapperFacade;

    @Autowired
    private ThreadPoolExecutor taskExecutor;

    @Autowired
    private SuperStarUserService userService;

    @Autowired
    private CourseUtil courseUtil;

    @Autowired
    private SuperStarLogService superStarLogService;

    @Autowired
    private SuperStarLoginService loginService;

    @Autowired
    private RollBackManager rb;

    @Autowired
    private ServerInfoUtil serverInfoUtil;

    @Autowired
    private ThreadPoolProperties config;

    @Autowired
    private StudyProperties studyProperties;


    @Autowired
    private StudyTaskControl taskControl;

    @Autowired(required = false)
    private LocalStudyProgress localProgress;

    private volatile boolean isShuttingDown = false;
    private final Object shutdownLock = new Object();


    public SuperStarTaskServiceImpl() throws NacosException {
    }

    public void shutdownThreadPoolGracefully() throws NacosException {
        // 提前检查减少同步块竞争
        if (isShuttingDown || taskExecutor.isShutdown()) {
            return;
        }

        synchronized (shutdownLock) {
            // 再次检查（双重检查锁模式）
            if (isShuttingDown || taskExecutor.isShutdown()) {
                return;
            }
            isShuttingDown = true;

            try {
                // 1. 停止接受新任务
                taskExecutor.shutdown();
                log.info("线程池开始关闭，等待现有任务完成...");

                // 2. 分阶段等待
                // 第一阶段：等待正常完成
                if (!taskExecutor.awaitTermination(1, TimeUnit.MINUTES)) {
                    log.warn("等待超时，尝试强制停止剩余任务...");

                    // 第二阶段：强制停止
                    List<Runnable> unfinishedTasks = taskExecutor.shutdownNow();

                    // 3. 处理未完成任务
                    if (!unfinishedTasks.isEmpty()) {
                        log.warn("有{}个任务被强制停止，开始回滚状态...", unfinishedTasks.size());
                        processUnfinishedTasks(unfinishedTasks);
                    }

                    // 最后检查
                    if (!taskExecutor.awaitTermination(1, TimeUnit.MINUTES)) {
                        log.error("线程池最终未能完全关闭");
                    }
                }
            } catch (InterruptedException e) {
                // 处理中断
                List<Runnable> unfinishedTasks = taskExecutor.shutdownNow();
                if (!unfinishedTasks.isEmpty()) {
                    processUnfinishedTasks(unfinishedTasks);
                }
                Thread.currentThread().interrupt();
                throw new NacosException(NacosException.SERVER_ERROR, "线程池关闭被中断");
            } finally {
                log.info("线程池关闭完成，当前状态: isShutdown={}, isTerminated={}",
                        taskExecutor.isShutdown(), taskExecutor.isTerminated());

            }
        }
    }

    private void processUnfinishedTasks(List<Runnable> unfinishedTasks) {
        unfinishedTasks.forEach(task -> {
            if (task instanceof PriorityTaskWrapper) {
                String taskId = ((PriorityTaskWrapper) task).getTaskId();
                try {
                    SuperStarTask dbTask = this.getById(taskId);
                    if (dbTask != null &&
                            (WkTaskStatusEnum.PROCESSING.getCode().equals(dbTask.getStatus()) ||
                                    WkTaskStatusEnum.EXAM.getCode().equals(dbTask.getStatus()))) {
                        dbTask.setStatus(WkTaskStatusEnum.PAUSED.getCode());
                        this.updateById(dbTask);
                    }
                } catch (Exception e) {
                    log.error("回滚任务状态失败, taskId={}", taskId, e);
                }
            }
        });
    }

    public void restartThreadPoolAndTasks() {
        synchronized (shutdownLock) {
            if (!taskExecutor.isShutdown()) {
                return;
            }

            BlockingQueue<Runnable> queue = new PriorityBlockingQueue<>(config.getQueueCapacity());

            // 重新构建线程池
            taskExecutor = new ThreadPoolExecutor(
                    config.getCoreSize(),
                    config.getMaxSize(),
                    config.getKeepAlive(),
                    TimeUnit.SECONDS,
                    queue,
                    new CustomThreadFactory(config.getThreadNamePrefix()),
                    new PriorityRejectPolicy()
            );
            taskExecutor.allowCoreThreadTimeOut(config.isAllowCoreThreadTimeout());
            this.startChaoxingTask();
        }
    }

    private SuperStarLog createNewLog(SuperStarTask task) {
        SuperStarLog log = mapperFacade.map(task, SuperStarLog.class);
        log.setStartTime(LocalDateTime.now());
        log.setStatus(WkTaskStatusEnum.PROCESSING.getCode());
        // 首次插入，获取自增ID
        superStarLogService.save(log);
        return log;
    }

    /**
     * 根据账号和课程id获取超星学习任务
     *
     * @param loginAccount 账号
     * @param courseId     课程id
     */
    @Override
    public SuperStarTask getSuperStarTask(String loginAccount, String courseId) {
        LambdaQueryWrapper<SuperStarTask> superStarTaskLambdaQueryWrapper = new LambdaQueryWrapper<>();
        superStarTaskLambdaQueryWrapper.eq(SuperStarTask::getLoginAccount, loginAccount);
        superStarTaskLambdaQueryWrapper.eq(SuperStarTask::getCourseId, courseId);
        return this.getOne(superStarTaskLambdaQueryWrapper);
    }

    /**
     * 根据账号和课程id获取超星学习任务
     *
     * @param loginAccount 账号
     * @param courseName   课程名
     */
    @Override
    public SuperStarTask getSuperStarTaskByCourseName(String loginAccount, String courseName) {
        LambdaQueryWrapper<SuperStarTask> superStarTaskLambdaQueryWrapper = new LambdaQueryWrapper<>();
        superStarTaskLambdaQueryWrapper.eq(SuperStarTask::getLoginAccount, loginAccount);
        superStarTaskLambdaQueryWrapper.eq(SuperStarTask::getCourseName, courseName);
        return this.getOne(superStarTaskLambdaQueryWrapper);
    }

    /**
     * 添加任务到等待队列
     *
     * @param courseSubmitTaskDTO 任务信息
     * @throws NacosException nacos异常
     */
    public void addChaoXingTask(CourseSubmitTaskDTO courseSubmitTaskDTO) throws NacosException {
        SuperStarTask task = null;
        if (courseSubmitTaskDTO.getCourseId() == null) {
            task = getSuperStarTaskByCourseName(courseSubmitTaskDTO.getLoginAccount(), courseSubmitTaskDTO.getCourseName());
        } else {
            task = getSuperStarTask(courseSubmitTaskDTO.getLoginAccount(), courseSubmitTaskDTO.getCourseId());
        }
        if (task == null) {
            SuperStarTask chaoXingTask = mapperFacade.map(courseSubmitTaskDTO, SuperStarTask.class);
            chaoXingTask.setStatus(WkTaskStatusEnum.PENDING.getCode());
            chaoXingTask.setId(LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMddHHmmssSSS"))); //当前version使用UUID
            chaoXingTask.setPriority(1);
            chaoXingTask.setCreatTime(LocalDateTime.now());
            chaoXingTask.setMachineNum(serverInfoUtil.getCurrentServerInstance());
            this.baseMapper.insert(chaoXingTask);
        } else {
            throw new BusinessException(BizCodeEnum.TASK_ALREADY_EXIST.getCode(), BizCodeEnum.TASK_ALREADY_EXIST.getMsg());
        }
        //TODO 这里似乎不太合理,等待后续优化.......
        startChaoxingTask();
    }

    /**
     * 超星学习任务启动
     */
    @Override
    public synchronized void startChaoxingTask() {
        LambdaQueryWrapper<SuperStarTask> wrapper = new LambdaQueryWrapper<>();
        wrapper.eq(SuperStarTask::getStatus, WkTaskStatusEnum.PENDING.getCode());
        wrapper.le(SuperStarTask::getRetryCount, RetryConstant.DEFAULT_RETRY_COUNT);
        wrapper.orderByDesc(SuperStarTask::getPriority);
        List<SuperStarTask> tasks = list(wrapper);
//        executeCourseTask(tasks.get(0));

        tasks.forEach(this::enqueueTask);
    }

    private void enqueueTask(SuperStarTask task) {
        if (taskControl.isActive(task.getId())) { return; }
        task.setStatus(WkTaskStatusEnum.QUEUE.getCode());
        try { task.setMachineNum(serverInfoUtil.getCurrentServerInstance()); }
        catch (NacosException e) { throw new RuntimeException(e); }
        this.updateById(task);
        try {
            taskControl.submit(task.getId(), task.getPriority() == null ? 1 : task.getPriority(), taskExecutor,
                    () -> executeCourseTask(task), () -> markTaskPaused(task));
        } catch (RuntimeException failure) {
            task.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
            this.updateById(task);
            throw failure;
        }
    }

    public synchronized void resumeTask(String id) {
        SuperStarTask task = this.getById(id);
        if (task == null) { throw new IllegalArgumentException("任务不存在"); }
        if (taskControl.isPauseRequested(id)) { throw new IllegalStateException("正在暂停，请稍后再开始"); }
        if (taskControl.isActive(id)) { return; }
        if (WkTaskStatusEnum.FINISHED.getCode().equals(task.getStatus())) {
            throw new IllegalStateException("此课程任务已完成");
        }
        task.setRetryCount(0);
        enqueueTask(task);
    }

    public synchronized void pauseTask(String id) {
        SuperStarTask task = this.getById(id);
        if (task == null) { throw new IllegalArgumentException("任务不存在"); }
        if (!taskControl.pause(id, taskExecutor)
                && !WkTaskStatusEnum.FINISHED.getCode().equals(task.getStatus())) {
            markTaskPaused(task);
        }
    }

    private void markTaskPaused(SuperStarTask task) {
        task.setStatus(WkTaskStatusEnum.PAUSED.getCode());
        this.updateById(task);
        SuperStarLog log = superStarLogService.getLatestLogByLoginAccount(task.getLoginAccount(), task.getCourseName());
        if (log != null) {
            log.setStatus(WkTaskStatusEnum.PAUSED.getCode());
            superStarLogService.updateById(log);
        }
    }

    /**
     * 执行超星学习任务
     *
     * @param task 超星学习任务
     */
    public void executeCourseTask(SuperStarTask task) {
        taskControl.checkpoint();
        task.setStatus(WkTaskStatusEnum.PROCESSING.getCode());
        this.updateById(task);
        WkUser user = userService.getUserByAccount(task.getLoginAccount());
        SuperStarLog log = superStarLogService.getLatestLogByLoginAccount(task.getLoginAccount(), task.getCourseName());

        if (log == null) {
            log = createNewLog(task);
        } else {
            log.setStartTime(LocalDateTime.now());
        }
        log.setStatus(WkTaskStatusEnum.PROCESSING.getCode());
        log.setErrorMessage(null);
        log.setEndTime(null);
        superStarLogService.updateById(log);


        if (user == null || user.getCookies() == null) {
            if (task.getRetryCount() < RetryConstant.DEFAULT_RETRY_COUNT) {
                task.setStatus(WkTaskStatusEnum.PENDING.getCode());
                task.setRetryCount(task.getRetryCount() + 1);
            } else {
                task.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
            }
            WkUser wkUser = new WkUser();
            wkUser.setAccount(task.getLoginAccount());
            wkUser.setPassword(task.getPassword());
            loginService.login(wkUser, true);
            this.updateById(task);
            log.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
            log.setErrorMessage("用户未登录");
            superStarLogService.updateById(log);
            return;
        }

        try {
            Course readyCourse = new Course();
            courseUtil.setAccount(task.getLoginAccount());
            courseUtil.setCookies(user.getCookies());
            if (task.getCourseId() != null) {
                readyCourse = courseUtil.getCourseList().stream()
                        .filter(course -> course.getCourseId().equals(task.getCourseId()))
                        .findFirst().orElse(null);
            } else if (task.getCourseName() != null) {
                List<Course> courseList = courseUtil.getCourseList();
                 readyCourse = Optional.ofNullable(courseList)
                        .orElse(Collections.emptyList())
                        .stream()
                        .filter(course -> course != null && task != null)
                        .filter(course -> {
                            // 标准化处理
                            String dbTitle = StringUtils.normalizeSpace(course.getTitle());
                            String taskName = StringUtils.normalizeSpace(task.getCourseName());

                            return dbTitle.equalsIgnoreCase(taskName);
                        })
                        .findFirst()
                        .orElse(null);
            }

            if (readyCourse == null) {
                loginService.login(user, true);
                log.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
                log.setErrorMessage(GlobalConstant.COURSE_INFO_GET_FAIL);
                superStarLogService.updateById(log);
                task.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
                this.updateById(task);
                return;
            }

            List<CoursePoint> pointList = courseUtil.getCoursePoint(
                    readyCourse.getCourseId(), readyCourse.getClazzId(), readyCourse.getCpi());

            processCoursePoints(task, log, readyCourse, pointList);
        } catch (Exception e) {
            failCourseTask(task, log, "课程处理失败: " + e.getMessage());
        }
    }

    private void failCourseTask(SuperStarTask task, SuperStarLog log, String message) {
        taskControl.checkpoint();
        log.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
        log.setErrorMessage(message.length() > 240 ? message.substring(0, 240) : message);
        log.setEndTime(LocalDateTime.now());
        superStarLogService.updateById(log);
        task.setStatus(WkTaskStatusEnum.ABNORMAL.getCode());
        this.updateById(task);
    }

    private void processCoursePoints(SuperStarTask task, SuperStarLog log, Course readyCourse, List<CoursePoint> pointList) {
        if (log == null) {
            log = createNewLog(task);
        }

        int pointIndex = log.getCurrentChapterIndex() != null ? log.getCurrentChapterIndex() : 0;

        if (pointList == null || pointList.isEmpty() || pointList.get(0) == null) {
            failCourseTask(task, log, "未读取到课程章节，无法判断完成状态");
            return;
        }
        List<ChapterPoint> chapterPointList = pointList.get(0).getPoints();
        if (chapterPointList == null || chapterPointList.isEmpty()) {
            failCourseTask(task, log, "未读取到课程章节，无法判断完成状态");
            return;
        }


        while (pointIndex < chapterPointList.size()) {
            taskControl.checkpoint();

            try {
                log.setCurrentChapterIndex(pointIndex);
                ChapterPoint chapterPoint = chapterPointList.get(pointIndex);
                if (localProgress != null) { localProgress.chapter(task.getId(), chapterPoint.getTitle(), chapterPointList.size()); }
                Pair<List<Job>, List<JobInfo>> result = courseUtil.getJobList(
                        readyCourse.getClazzId().toString(),
                        readyCourse.getCourseId(),
                        readyCourse.getCpi(),
                        chapterPoint.getId());

                taskControl.checkpoint();
                List<Job> jobs = result.getFirst();
                List<JobInfo> jobInfo = result.getSecond();

                if (jobInfo.stream().anyMatch(info -> Boolean.TRUE.equals(info.getNotOpen()))) {
                    log.setStatus(WkTaskStatusEnum.PAUSED.getCode());
                    log.setRemark("章节未开放，已暂停");
                    superStarLogService.updateById(log);
                    task.setStatus(WkTaskStatusEnum.PAUSED.getCode());
                    this.updateById(task);
                    return;
                }

                if (jobs.isEmpty()) {
                    pointIndex++;
                    log.setCurrentChapterIndex(pointIndex);
                    log.setCurrentProgress(BigDecimal.valueOf(pointIndex * 100.0 / chapterPointList.size()));
                    superStarLogService.updateById(log);
                    continue;
                }

                for (Job job : jobs) {
                    taskControl.checkpoint();
                    try {

                        switch (job.getType()) {

                            case JobTypeConstant.VIDEO:
                                courseUtil.studyVideo(readyCourse, job, jobInfo.get(0), studyProperties.getSpeed(), "Video", log);
                                break;
                            case JobTypeConstant.DOCUMENT:
                                courseUtil.studyDocument(readyCourse, job);
                                break;
                            case JobTypeConstant.READ:
                                courseUtil.studyRead(readyCourse, job, jobInfo.get(0), log);
                                break;
                            case JobTypeConstant.QUESTION:
                                courseUtil.studyWork(readyCourse, job, jobInfo.get(0), log);
                            default:
                                break;

                        }

                    } catch (Exception e) {
                        failCourseTask(task, log, "任务处理失败: " + e.getMessage());
                        return;
                    }
                }
                taskControl.checkpoint();
                pointIndex++;

                int totalChapters = chapterPointList.size();
                double currentPercentage = (pointIndex * 100.0) / totalChapters;
                log.setCurrentChapterIndex(pointIndex);
                log.setCurrentProgress(BigDecimal.valueOf(currentPercentage));
                superStarLogService.updateById(log);


            } catch (Exception e) {
                failCourseTask(task, log, "章节处理失败: " + e.getMessage());
                return;
            }
        }

        taskControl.checkpoint();
        if (pointIndex >= chapterPointList.size()) {
            log.setEndTime(LocalDateTime.now());
            log.setCurrentChapterIndex(pointIndex);
            log.setCurrentProgress(BigDecimal.valueOf(100));
            log.setStatus(WkTaskStatusEnum.FINISHED.getCode());

            superStarLogService.updateById(log);
            task.setStatus(WkTaskStatusEnum.FINISHED.getCode());
            this.updateById(task);
        }
    }
}
