using System.Text.Json.Nodes;

namespace IronST.LanguageServer.JsonRpc;

/// <summary>JSON-RPC 2.0 消息构造工具。</summary>
internal static class RpcMessages
{
    /// <summary>成功响应。</summary>
    public static JsonObject Response(JsonNode? id, JsonNode? result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result?.DeepClone(),
        };

    /// <summary>错误响应。code 约定见 <see cref="RpcErrorCodes"/>。</summary>
    public static JsonObject ErrorResponse(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };

    /// <summary>通知（无 id，不需要响应）。</summary>
    public static JsonObject Notification(string method, JsonNode? @params) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params?.DeepClone(),
        };
}

/// <summary>JSON-RPC 标准错误码。</summary>
internal static class RpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}
