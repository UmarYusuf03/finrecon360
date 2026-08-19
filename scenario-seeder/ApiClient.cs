using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScenarioSeeder;

/// <summary>
/// Thin HTTP wrapper around the finrecon360-backend API. Every scenario row this tool creates goes
/// through the real endpoints (auth, tenant onboarding, transactions, imports) rather than touching
/// the database directly, so the data is normalized/validated exactly the way production traffic
/// would be — that's the whole point of driving the API instead of hand-writing rows.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public void SetToken(string token) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public void ClearToken() => _http.DefaultRequestHeaders.Authorization = null;

    public void SetTenant(Guid tenantId)
    {
        _http.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
    }

    public async Task<T?> PostAsync<T>(string path, object? body)
    {
        HttpContent? content = body is null
            ? null
            : new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content);
        return await ReadAsync<T>(response, "POST " + path);
    }

    public async Task<T?> PutAsync<T>(string path, object? body)
    {
        HttpContent? content = body is null
            ? null
            : new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PutAsync(path, content);
        return await ReadAsync<T>(response, "PUT " + path);
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path);
        return await ReadAsync<T>(response, "GET " + path);
    }

    public async Task<T?> PostMultipartAsync<T>(
        string path, string fileName, byte[] fileBytes, string sourceType, Guid? bankAccountId)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(sourceType), "sourceType");
        if (bankAccountId.HasValue)
        {
            form.Add(new StringContent(bankAccountId.Value.ToString()), "bankAccountId");
        }

        using var response = await _http.PostAsync(path, form);
        return await ReadAsync<T>(response, "POST(multipart) " + path);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string label)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{label} -> {(int)response.StatusCode} {response.ReasonPhrase}: {text}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }
}
