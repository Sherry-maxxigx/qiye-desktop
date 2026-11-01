package com.tihai.config;

import lombok.Data;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.context.annotation.Configuration;

import java.util.ArrayList;
import java.util.List;


/**
 * @Copyright : DuanInnovator
 * @Description : 题库配置类
 * @Author : DuanInnovator
 * @CreateTime : 2025/4/24
 * @Link : <a href="https://github.com/DuanInnovator/TiHaiWuYou-Admin/tree/mine-admin">...</a>
 **/
@Data
@Configuration
@ConfigurationProperties(prefix = "tiku.settings")
public class TiKuConfig {

    private List<Endpoint> endpoints=new ArrayList<>();
    private String baseUrl = "http://tk.xxtmooc.com/api/q";
    private String provider = "super";
    /** Optional user-supplied question data; not bundled with public releases. */
    private String localBankFile = "";
    private String cxmoocUrl = "https://cx.icodef.com/v2/answer?platform=cx";
    private String cxmoocToken = "";
    private boolean buguakeEnabled = false;
    private String buguakeUrl = "https://easylearn.baidu.com/edu-web-go/bgk/searchlist";

    @Data
    public static class Endpoint {
        /**
         * 题库名称
         **/
        private String name;

        /**
         * token
         **/
        private String token;
    }


}
