namespace LightReader;

public static class ConfigurationChecks
{
    public static string? ModelError(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "请填写支持图片输入的模型 ID，或点击读取模型列表。";
        if (model.Trim().StartsWith("apikey-", StringComparison.OrdinalIgnoreCase))
            return "这里填的是 API Key 的资源 ID，不是模型 ID。请从模型列表选择支持图片输入的豆包模型，或填写 ep- 开头的推理接入点 ID。";
        if (model.Any(char.IsWhiteSpace)) return "模型 ID 不能包含空格或换行，请复制完整的 Model ID。";
        return null;
    }
    public static string? OcrError(Settings settings) => string.IsNullOrWhiteSpace(settings.ArkKey)
        ? "请填写火山方舟 API Key。" : ModelError(settings.Model);

    public static string SpeechHandshakeError(int status) => status switch
    {
        401 => "Seeduplex 鉴权失败（401）：请使用豆包语音控制台的 API Key，并开通 Seeduplex 全双工服务。火山方舟 Chat API 的 Key 不能代替语音服务凭证。",
        403 => "Seeduplex 无访问权限（403）：请检查语音服务开通状态、密钥权限和账户额度。",
        429 => "Seeduplex 请求受限（429）：请稍后重试并检查账户配额。",
        _ => $"Seeduplex 连接失败（HTTP {status}），请检查网络与语音服务配置。"
    };
}
