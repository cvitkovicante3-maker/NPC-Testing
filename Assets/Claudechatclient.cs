using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>One message in a conversation. role is "user" or "assistant".</summary>
[Serializable]
public class ChatMessage
{
    public string role;
    public string content;

    public ChatMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

/// <summary>
/// Minimal client for the Claude Messages API (https://api.anthropic.com/v1/messages).
/// Used by NPCDialogue. Runs as a coroutine, so it never freezes the game.
/// </summary>
public static class ClaudeChatClient
{
    private const string Url = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    [Serializable]
    public class RequestBody
    {
        public string model;
        public int max_tokens;
        public string system;
        public List<ChatMessage> messages;
    }

    [Serializable] public class ContentBlock { public string type; public string text; }
    [Serializable] public class ResponseBody { public ContentBlock[] content; }
    [Serializable] public class ErrorInfo { public string type; public string message; }
    [Serializable] public class ErrorBody { public ErrorInfo error; }

    /// <summary>
    /// Looks for the key in the ANTHROPIC_API_KEY environment variable first,
    /// then falls back to the (testing-only) value from the Inspector.
    /// </summary>
    public static string ResolveKey(string inspectorFallback)
    {
        string env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        return string.IsNullOrWhiteSpace(inspectorFallback) ? null : inspectorFallback.Trim();
    }

    public static IEnumerator Send(
        string apiKey, string model, string system, List<ChatMessage> messages, int maxTokens,
        Action<string> onReply, Action<string> onError)
    {
        var body = new RequestBody
        {
            model = model,
            max_tokens = maxTokens,
            system = system,
            messages = messages
        };
        byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

        using (var req = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", ApiVersion);
            req.timeout = 30;

            yield return req.SendWebRequest();

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result == UnityWebRequest.Result.Success)
            {
                ResponseBody resp = null;
                try { resp = JsonUtility.FromJson<ResponseBody>(text); } catch (Exception) { }

                var sb = new StringBuilder();
                if (resp != null && resp.content != null)
                    foreach (ContentBlock block in resp.content)
                        if (block.type == "text") sb.Append(block.text);

                string reply = sb.ToString().Trim();
                if (reply.Length == 0) onError?.Invoke("The reply came back empty.");
                else onReply?.Invoke(reply);
            }
            else
            {
                onError?.Invoke(DescribeError(req, text));
            }
        }
    }

    private static string DescribeError(UnityWebRequest req, string body)
    {
        long code = req.responseCode;
        if (code == 0)
            return "Couldn't reach the API (" + req.error + "). Check your internet connection.";

        string detail = null;
        try
        {
            ErrorBody e = JsonUtility.FromJson<ErrorBody>(body);
            if (e != null && e.error != null) detail = e.error.message;
        }
        catch (Exception) { }

        string hint =
            code == 401 ? " The API key was rejected." :
            code == 429 ? " Too many requests - wait a moment." :
            (code == 503 || code == 529) ? " The API is busy - try again." : "";

        return ("API error " + code + "." + hint + " " + detail).Trim();
    }
}