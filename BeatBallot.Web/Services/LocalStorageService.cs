using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace BeatBallot.Web.Services
{
    public interface ILocalStorageService
    {
        public Task<T> GetItemAsync<T>(string key);

        public Task RemoveItemAsync(string key);

        public Task StoreStateAsync<T>(string key, T value);

    }

    public class LocalStorageService : ILocalStorageService
    {
        private readonly IJSRuntime jsruntime;
        public LocalStorageService(IJSRuntime jSRuntime)
        {
            jsruntime = jSRuntime;
        }


        public async Task RemoveItemAsync(string key)
        {
            await jsruntime.InvokeVoidAsync("localStorage.removeItem", key).ConfigureAwait(false);
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
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                // Compress the bytes
                byte[] compressedBytes = await Compressor.CompressBytesAsync(bytes);

                // Encode to base64
                string compressedBase64 = Convert.ToBase64String(compressedBytes);

                // Store in localStorage
                await jsruntime.InvokeVoidAsync("localStorage.setItem", key, compressedBase64)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving item '{key}' to localStorage: {ex.Message}");
                throw;
            }
        }


        public async Task<T> GetItemAsync<T>(string key)
        {
            try
            {
                // Get the compressed, base64-encoded string from localStorage
                string compressedBase64 = await jsruntime.InvokeAsync<string>("localStorage.getItem", key)
                    .ConfigureAwait(false);

                // Return default if nothing stored
                if (string.IsNullOrEmpty(compressedBase64))
                    return default(T);

                // Decode from base64
                byte[] compressedBytes = Convert.FromBase64String(compressedBase64);

                // Decompress the bytes
                byte[] decompressedBytes = await Compressor.DecompressBytesAsync(compressedBytes);

                // Convert bytes back to string
                string json = Encoding.UTF8.GetString(decompressedBytes);

                // Handle different types
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)json;
                }

                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(json, out int intValue))
                        return (T)(object)intValue;
                    return default(T);
                }

                if (typeof(T) == typeof(DateTime))
                {
                    if (DateTime.TryParse(json, out DateTime dateValue))
                        return (T)(object)dateValue;
                    return default(T);
                }

                if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(json, out bool boolValue))
                        return (T)(object)boolValue;
                    return default(T);
                }

                // For complex objects, deserialize JSON
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                // Log the exception and return default
                Console.WriteLine($"Error getting item '{key}' from localStorage: {ex.Message}");
                return default(T);
            }
        }

      
    }

    internal class Compressor
    {
        public static async Task<byte[]> CompressBytesAsync(byte[] bytes)
        {
            using MemoryStream outputStream = new MemoryStream();
            using (GZipStream compressionStream = new GZipStream(outputStream, CompressionLevel.Optimal))
            {
                await compressionStream.WriteAsync(bytes, 0, bytes.Length);
            }
            return outputStream.ToArray();
        }

        public static async Task<byte[]> DecompressBytesAsync(byte[] bytes)
        {
            using MemoryStream inputStream = new MemoryStream(bytes);
            using MemoryStream outputStream = new MemoryStream();
            using (GZipStream compressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                await compressionStream.CopyToAsync(outputStream);
            }
            return outputStream.ToArray();
        }
    }
}
