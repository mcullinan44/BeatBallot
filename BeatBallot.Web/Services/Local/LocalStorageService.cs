using Microsoft.JSInterop;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BeatBallot.Web.Services.Local
{
    public interface ILocalStorageService
    {
        public Task<T> GetItemAsync<T>(string key);

        public Task RemoveItemAsync(string key);

        public Task StoreStateAsync<T>(string key, T value);

    }

    public class LocalStorageService : ILocalStorageService
    {
        private readonly IJSRuntime _jsruntime;
        public LocalStorageService(IJSRuntime jSRuntime)
        {
            _jsruntime = jSRuntime;
        }


        public async Task RemoveItemAsync(string key)
        {
            await _jsruntime.InvokeVoidAsync("localStorage.removeItem", key).ConfigureAwait(false);
        }


        public async Task StoreStateAsync<T>(string key, T value)
        {
            try
            {
                string json;

                // Handle different types
                if (value is string stringValue)
                {
                    json = stringValue;
                }
                else if (value is int || value is DateTime || value is bool)
                {
                    json = value.ToString();
                }
                else
                {
                    // Serialize complex objects to JSON
                    json = JsonSerializer.Serialize(value);
                }

                // Convert to bytes
                var bytes = Encoding.UTF8.GetBytes(json);

                // Compress the bytes
                var compressedBytes = await Compressor.CompressBytesAsync(bytes);

                // Encode to base64
                var compressedBase64 = Convert.ToBase64String(compressedBytes);

                // Store in localStorage
                await _jsruntime.InvokeVoidAsync("localStorage.setItem", key, compressedBase64)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving item '{key}' to localStorage: {ex.Message}");
                throw;
            }
        }

        public async Task StoreLiveDjSession(string sessionId, string playListId)
        {

        
        


        }


        public async Task<T> GetItemAsync<T>(string key)
        {
            try
            {
                // Get the compressed, base64-encoded string from localStorage
                var compressedBase64 = await _jsruntime.InvokeAsync<string>("localStorage.getItem", key)
                    .ConfigureAwait(false);

                // Return default if nothing stored
                if (string.IsNullOrEmpty(compressedBase64))
                    return default;

                // Decode from base64
                var compressedBytes = Convert.FromBase64String(compressedBase64);

                // Decompress the bytes
                var decompressedBytes = await Compressor.DecompressBytesAsync(compressedBytes);

                // Convert bytes back to string
                var json = Encoding.UTF8.GetString(decompressedBytes);

                // Handle different types
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)json;
                }

                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(json, out var intValue))
                        return (T)(object)intValue;
                    return default;
                }

                if (typeof(T) == typeof(DateTime))
                {
                    if (DateTime.TryParse(json, out var dateValue))
                        return (T)(object)dateValue;
                    return default;
                }

                if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(json, out var boolValue))
                        return (T)(object)boolValue;
                    return default;
                }

                // For complex objects, deserialize JSON
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                // Log the exception and return default
                Console.WriteLine($"Error getting item '{key}' from localStorage: {ex.Message}");
                return default;
            }
        }

      
    }

    internal class Compressor
    {
        public static async Task<byte[]> CompressBytesAsync(byte[] bytes)
        {
            using var outputStream = new MemoryStream();
            using (var compressionStream = new GZipStream(outputStream, CompressionLevel.Optimal))
            {
                await compressionStream.WriteAsync(bytes, 0, bytes.Length);
            }
            return outputStream.ToArray();
        }

        public static async Task<byte[]> DecompressBytesAsync(byte[] bytes)
        {
            using var inputStream = new MemoryStream(bytes);
            using var outputStream = new MemoryStream();
            using (var compressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                await compressionStream.CopyToAsync(outputStream);
            }
            return outputStream.ToArray();
        }
    }
}
