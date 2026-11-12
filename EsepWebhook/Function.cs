using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    private static readonly HttpClient HttpClient = new();

    public async Task<string> FunctionHandler(JsonElement input, ILambdaContext context)
    {
        // GitHub sends a ping when the webhook is created. Only issue events have this field.
        if (input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("issue", out var issue) ||
            !issue.TryGetProperty("html_url", out var issueUrlElement) ||
            issueUrlElement.ValueKind != JsonValueKind.String)
        {
            return "ignored";
        }

        var issueUrl = issueUrlElement.GetString();
        if (!Uri.TryCreate(issueUrl, UriKind.Absolute, out var issueUri) ||
            issueUri.Scheme != Uri.UriSchemeHttps ||
            issueUri.Host != "github.com")
        {
            return "ignored";
        }

        var slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        if (string.IsNullOrWhiteSpace(slackUrl))
        {
            throw new InvalidOperationException("SLACK_URL is not configured.");
        }

        var payload = JsonSerializer.Serialize(new { text = $"Issue Created: {issueUrl}" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(slackUrl, content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        if (result != "ok")
        {
            throw new InvalidOperationException($"Slack rejected the message: {result}");
        }

        context.Logger.LogLine("Posted issue link to Slack.");
        return result;
    }
}
