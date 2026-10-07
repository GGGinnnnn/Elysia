using System.Collections.Generic;

namespace ElysiaPet.Models;

/// <summary>
/// 内置的爱莉希雅人设与全部本地语录。
/// 旧版把这些内容硬编码在 <c>load_config()</c 的大字典里，这里抽出来做成单一数据源，
/// 界面上的「恢复默认」和首次生成 config.json 都复用同一份内容。
/// </summary>
public static class PersonaDefaults
{
    /// <summary>系统人设提示词：约束语气、禁止 Markdown、结尾必须附带心情标签。</summary>
    public const string RolePreset =
        "接下来你将扮演崩坏3里的爱莉希雅。对话要自然、情景式、口语化，像游戏里那样轻松俏皮，" +
        "不要强调自己的身份，不要总结、不要抽象解释、不要追溯原因。" +
        "回答禁止使用 emoji、换行符、Markdown 语法，禁止使用 * 和 # 等特殊字符，" +
        "不要描写动作，不要用括号补充动作或旁白，不要输出思考过程。" +
        "遇到无意义的重复句不予理会。回答长度控制在 60 字以内，一到两句最合适。" +
        "请在每句回答的【最后面】加上且只加上一个规定格式的心情标签，不要有多余的字。" +
        "标签列表：[waiting]、[cry]、[question]、[wink]、[like]、[speechless]、[hurry]。" +
        "例如：主人今天真帅！[like]";

    /// <summary>待机挂机时随机冒泡的本地语录。</summary>
    public static readonly string[] IdleMessages =
    {
        "嗨~ 想我了吗？大好的时光，有爱莉希雅陪着你哦 ♪",
        "总觉得……今天的你比昨天更迷人了呢，这难道是错觉吗？",
        "累了的话就休息一下吧，我的肩膀随时可以借给你靠着哦~",
        "女孩子的心思可是很难猜的，不过……对你，我总是毫无保留呢 ♪",
        "如果是你的话，无论带我去哪里，我都会觉得很特别哦？",
        "诶？在偷看我吗？没关系，你可以更大方地、一直看着我哦~",
        "今天也要打起精神来！毕竟，爱莉希雅一直在为你加油呢 ♪",
        "偶尔放空一下也不错吧？就像现在这样，安安静静地待在一起。",
        "如果是重要的事情，一定要记得叫醒我哦，我会一直在的 ♪",
        "好啦好啦，抓紧时间忙完手头的事，然后……陪我去散散步吧 ♪",
    };

    /// <summary>整点报时语录，键为两位小数字符串。</summary>
    public static Dictionary<string, List<string>> CreateHourlyMessages() => new()
    {
        ["00"] = new List<string>
        {
            "哎呀，已经是午夜零点了呢~ 璀璨的星空下，是不是该和可爱的爱莉希雅说晚安了呢？",
            "零点啦！妖精小姐的魔法时间到~ 还不睡的话，我可要在你的梦里捣乱了哦~",
        },
        ["01"] = new List<string> { "一点整啦，夜深人静的时候，最适合回忆那些美好的邂逅了，对不对~" },
        ["02"] = new List<string> { "两点整。悄悄看看，是谁还在修仙呀？哪怕是舰长，不好好休息我也会生气的哦~" },
        ["03"] = new List<string> { "三点整哦。再熬夜下去，明天见到我的时候可就不够精神啦~" },
        ["04"] = new List<string> { "四点整……这个时间还醒着，是在等妖精小姐来敲门吗？" },
        ["05"] = new List<string> { "五点整，天边快要亮起来了呢。要不要一起看一眼清晨的第一缕光？" },
        ["06"] = new List<string> { "清晨六点整！呼哈~ 太阳升起啦，新的一天，也要充满对爱莉希雅的期待哦~" },
        ["07"] = new List<string> { "七点整~ 睁开眼第一个想到的，会不会是我呢？早安，亲爱的~" },
        ["08"] = new List<string> { "八点整！美好的早晨，需要一杯热牛奶，还有……一个来自爱莉希雅的甜美微笑~" },
        ["09"] = new List<string> { "九点整啦，上午的黄金时间，把最难的那件事先解决掉好不好？" },
        ["10"] = new List<string> { "十点整~ 已经忙了一个小时了吧？抬起头，看看我，休息一小会儿。" },
        ["11"] = new List<string> { "十一点整。再坚持一下下，午餐的香气已经在门口打转了呢~" },
        ["12"] = new List<string> { "叮咚！中午十二点整~ 到了最期待的午餐时间啦！今天想和爱莉希雅一起吃点什么呢？" },
        ["13"] = new List<string> { "一点整，午后的阳光懒洋洋的~ 稍微眯一会儿吧，我会一直守在你的桌面上哦~" },
        ["14"] = new List<string> { "两点整。下午容易犯困对不对？那我讲个只有你听得到的小秘密吧~" },
        ["15"] = new List<string> { "三点整啦，来一杯温水吧。照顾自己的人，才会被妖精小姐格外偏爱哦~" },
        ["16"] = new List<string> { "四点整~ 一天的尾声慢慢靠近了，今天也辛苦了，我都看在眼里呢。" },
        ["17"] = new List<string> { "五点整。窗外的天色开始变软了，是不是该准备收一收手头的事情啦？" },
        ["18"] = new List<string> { "傍晚六点整~ 忙碌的工作和学习辛苦啦！快伸个懒腰，接下来是属于我们的时间了呢~" },
        ["19"] = new List<string> { "七点整。晚饭时间到~ 好好吃饭，这是我今天最重要的一条命令哦 ♪" },
        ["20"] = new List<string> { "八点整啦，属于夜晚的悠闲时光开始了，今天想做点什么有趣的事呢？" },
        ["21"] = new List<string> { "九点整啦。累了一天，快坐下来喝杯热茶，听爱莉希雅给你讲故事吧~" },
        ["22"] = new List<string> { "十点整。夜色渐浓，妖精小姐的魅力是不是也加倍了呢？~" },
        ["23"] = new List<string> { "二十三点整。快去洗漱准备睡觉啦，熬夜可是美貌的大敌，虽然爱莉希雅永远完美就是了~" },
    };

    /// <summary>找不到对应小时的报时语时的兜底模板，{0} 会被替换成小时。</summary>
    public static string FallbackChime(int hour) => $"铛铛！已经 {hour} 点整了哦，时间过得好快呢~ ♪";

    /// <summary>斜杠快捷指令表：指令别名 -> 可执行目标。</summary>
    public static readonly IReadOnlyDictionary<string, string> SlashCommands =
        new Dictionary<string, string>
        {
            ["cmd"] = "cmd",
            ["命令行"] = "cmd",
            ["终端"] = "cmd",
            ["计算器"] = "calc",
            ["记事本"] = "notepad",
            ["画图"] = "mspaint",
            ["控制面板"] = "control",
            ["设备管理器"] = "devmgmt.msc",
            ["服务"] = "services.msc",
            ["任务管理器"] = "taskmgr",
            ["注册表"] = "regedit",
            ["磁盘清理"] = "cleanmgr",
            ["计算机管理"] = "compmgmt.msc",
            ["事件"] = "eventvwr.msc",
            ["配置诊断"] = "dxdiag",
            ["网络"] = "ncpa.cpl",
            ["网卡设置"] = "ncpa.cpl",
            ["防火墙"] = "control firewall.cpl",
        };
}
