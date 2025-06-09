//Exercise: Elaborar un programa que, utilizando cualquier librería para hacer peticiones http, descargue todos los diarios oficiales de El Salvador disponibles en internet. El programa debe imprimir en un archivo: la cantidad de diarios, cuántos hay por año y permitir la impresión del reporte en pantalla o en archivo de texto. El programa debe gestionar los problemas de conectividad, en caso falle la descarga o conexión a internet.

/*
Url del sitio web:
https://www.diariooficial.gob.sv/
Url de la API:
https://www.diariooficial.gob.sv/api/v1/meses-disponibles
https://www.diariooficial.gob.sv/api/v1/diarios-disponibles

Los años estan directamente fijos en el list del html (1847 - 2025)
La API de /diarios-disponibles esperaba los datos como x-www-form-urlencoded.
Esta API de /meses-disponibles espera los datos como raw JSON.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2; 
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Threading;

namespace DiariosElSalvador
{
    // Models for JSON deserialization
    public class MesDisponible
    {
        [JsonPropertyName("month")]
        public string? Month { get; set; }
    }

    public class Diario
    {
        [JsonPropertyName("Id")]
        public string? Id { get; set; }

        [JsonPropertyName("FechaInicio")]
        public string? FechaInicio { get; set; } // "YYYY-MM-DD"

        [JsonPropertyName("FechaInexacta")]
        public string? FechaInexacta { get; set; }

        [JsonPropertyName("NombreArchivo")]
        public string? NombreArchivo { get; set; } // "DD-MM-YYYY.pdf"
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string BaseUrl = "https://www.diariooficial.gob.sv";
        private const string MesesDisponiblesApi = "/api/v1/meses-disponibles";
        private const string DiariosDisponiblesApi = "/api/v1/diarios-disponibles";
        // Corrected Download Base URL - the ID will be appended directly
        private const string DiarioDownloadBaseUrl = "https://www.diariooficial.gob.sv/seleccion/"; 
        private const string OutputDirectory = "DiariosOficiales";

        static async Task Main(string[] args)
        {
            Console.WriteLine("Iniciando descarga de Diarios Oficiales de El Salvador...");
            // Ensure credentials.json is in the output directory (e.g., bin/Debug/net8.0)
            if (!File.Exists("credentials.json"))
            {
                Console.WriteLine("ERROR: El archivo 'credentials.json' no se encuentra en el directorio de ejecución.");
                Console.WriteLine("Por favor, asegúrate de que 'credentials.json' esté copiado en el directorio de salida (por ejemplo, junto al .exe).");
                Console.WriteLine("Presione cualquier tecla para salir.");
                Console.ReadKey();
                return;
            }
            Directory.CreateDirectory(OutputDirectory);

            List<Diario> todosLosDiariosDescargados = new List<Diario>();
            Dictionary<int, int> diariosDescargadosPorAnio = new Dictionary<int, int>();

            int startYear = 1847;
            int endYear = 2025; // As per exercise description for available years

            for (int year = startYear; year <= endYear; year++)
            {
                Console.WriteLine($"Procesando año: {year}");
                List<MesDisponible> meses = await GetAvailableMonthsAsync(year);
                
                // meses will not be null due to changes in GetAvailableMonthsAsync
                if (!meses.Any())
                {
                    Console.WriteLine($"No hay meses disponibles para el año {year} o la API no retornó datos (o hubo un error previo).");
                    continue;
                }

                // Initialize count for the year if not present
                if (!diariosDescargadosPorAnio.ContainsKey(year))
                {
                    diariosDescargadosPorAnio[year] = 0;
                }

                foreach (var mesObj in meses)
                {
                    if (string.IsNullOrEmpty(mesObj.Month) || !int.TryParse(mesObj.Month, out int month))
                    {
                        Console.WriteLine($"  Mes inválido o nulo '{mesObj.Month}' para el año {year}. Saltando.");
                        continue;
                    }
                    Console.WriteLine($"  Procesando mes: {month}");
                    List<Diario> diariosDelMes = await GetAvailableDiariosAsync(year, month);

                    // diariosDelMes will not be null due to changes in GetAvailableDiariosAsync
                    if (!diariosDelMes.Any())
                    {
                        Console.WriteLine($"  No hay diarios disponibles para {year}-{month:D2} o la API no retornó datos (o hubo un error previo).");
                        continue;
                    }

                    foreach (var diario in diariosDelMes)
                    {
                        if (string.IsNullOrEmpty(diario.NombreArchivo) || string.IsNullOrEmpty(diario.Id)) // Check Id as well for download URL
                        {
                            Console.WriteLine("    Nombre de archivo o ID del diario es nulo o vacío. Saltando.");
                            continue;
                        }
                        Console.WriteLine($"    Intentando descargar: {diario.NombreArchivo}");
                        bool descargado = await DownloadDiarioAsync(diario, year, month);
                        if (descargado)
                        {
                            todosLosDiariosDescargados.Add(diario);
                            diariosDescargadosPorAnio[year]++; // Increment count for the year
                            Console.WriteLine($"    Descargado exitosamente: {diario.NombreArchivo}");

                            // Subir a Google Drive
                            string filePathLocal = Path.Combine(OutputDirectory, year.ToString(), month.ToString("D2"), diario.NombreArchivo);
                            Console.WriteLine($"    Intentando subir a Google Drive: {diario.NombreArchivo}");
                            try
                            {
                                await EjemploDrive.UploadFileAsync(filePathLocal, year.ToString(), month.ToString("D2"));
                                Console.WriteLine($"    Subido exitosamente a Google Drive: {diario.NombreArchivo}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"    Fallo al subir a Google Drive {diario.NombreArchivo}: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    Fallo al descargar: {diario.NombreArchivo}");
                        }
                        await Task.Delay(200); // Be polite to the server
                    }
                }
            }

            Console.WriteLine("\nProceso de descarga completado.");
            GenerarReporte(todosLosDiariosDescargados, diariosDescargadosPorAnio);

            Console.WriteLine("\nPresione cualquier tecla para salir.");
            Console.ReadKey();
        }

        private static async Task<List<MesDisponible>> GetAvailableMonthsAsync(int year)
        {
            try
            {
                var payload = new { year = year.ToString() };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                
                HttpResponseMessage response = await httpClient.PostAsync(BaseUrl + MesesDisponiblesApi, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var meses = await response.Content.ReadFromJsonAsync<List<MesDisponible>>();
                    return meses ?? new List<MesDisponible>(); // Return empty list if null
                }
                else
                {
                    Console.WriteLine($"Error al obtener meses para el año {year}. Status: {response.StatusCode}, Razón: {response.ReasonPhrase}");
                    // var errorContent = await response.Content.ReadAsStringAsync(); // Uncomment for debugging
                    // Console.WriteLine($"Contenido del error: {errorContent}");      // Uncomment for debugging
                    return new List<MesDisponible>(); // Return empty list on HTTP error to allow processing other years
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error de conexión al obtener meses para el año {year}: {e.Message}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"Error al deserializar JSON de meses para el año {year}: {e.Message}");
            }
            catch (Exception e) // Catch-all for other unexpected errors
            {
                Console.WriteLine($"Error inesperado al obtener meses para el año {year}: {e.Message}");
            }
            return new List<MesDisponible>(); // Return empty list on any failure to allow continuation
        }

        private static async Task<List<Diario>> GetAvailableDiariosAsync(int year, int month)
        {
            try
            {
                var payload = new Dictionary<string, string>
                {
                    { "year", year.ToString() },
                    { "month", month.ToString() }
                };
                var content = new FormUrlEncodedContent(payload);
                
                HttpResponseMessage response = await httpClient.PostAsync(BaseUrl + DiariosDisponiblesApi, content);

                if (response.IsSuccessStatusCode)
                {
                    var diarios = await response.Content.ReadFromJsonAsync<List<Diario>>();
                    return diarios ?? new List<Diario>(); // Return empty list if null
                }
                else
                {
                    Console.WriteLine($"Error al obtener diarios para {year}-{month:D2}. Status: {response.StatusCode}, Razón: {response.ReasonPhrase}");
                    // var errorContent = await response.Content.ReadAsStringAsync(); // Uncomment for debugging
                    // Console.WriteLine($"Contenido del error: {errorContent}");      // Uncomment for debugging
                    return new List<Diario>(); // Return empty list on HTTP error
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error de conexión al obtener diarios para {year}-{month:D2}: {e.Message}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"Error al deserializar JSON de diarios para {year}-{month:D2}: {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error inesperado al obtener diarios para {year}-{month:D2}: {e.Message}");
            }
            return new List<Diario>(); // Return empty list on any failure
        }

        private static async Task<bool> DownloadDiarioAsync(Diario diario, int year, int month)
        {
            // Null checks for diario, NombreArchivo, and Id are done in the main loop before calling this.
            // However, an additional check here is good practice.
            if (diario == null || string.IsNullOrEmpty(diario.NombreArchivo) || string.IsNullOrEmpty(diario.Id))
            {
                Console.WriteLine("    Información del diario (diario, NombreArchivo o Id) inválida para la descarga.");
                return false;
            }
            // Corrected download URL using the ID
            string downloadUrl = $"{DiarioDownloadBaseUrl}{diario.Id}";
            
            string monthPadded = month.ToString("D2");
            string yearDirectory = Path.Combine(OutputDirectory, year.ToString());
            string monthDirectory = Path.Combine(yearDirectory, monthPadded);
            Directory.CreateDirectory(monthDirectory); 

            string filePath = Path.Combine(monthDirectory, diario.NombreArchivo);

            if (File.Exists(filePath))
            {
                Console.WriteLine($"    Archivo ya existe: {filePath}. Saltando descarga.");
                // If file exists, we consider it "downloaded" for the report purposes if it was from a previous run.
                // However, the current logic adds to `todosLosDiariosDescargados` only on fresh successful download.
                // For simplicity, we'll return true, assuming it's a valid existing file.
                return true; 
            }

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(downloadUrl);
                if (response.IsSuccessStatusCode)
                {
                    using (var fs = new FileStream(filePath, FileMode.CreateNew))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    return true;
                }
                else
                {
                    Console.WriteLine($"    Error al descargar {diario.NombreArchivo}. URL: {downloadUrl}. Status: {response.StatusCode}, Razón: {response.ReasonPhrase}");
                    return false;
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"    Error de conexión al descargar {diario.NombreArchivo} de {downloadUrl}: {e.Message}");
            }
            catch (IOException e) // Handles file system errors like disk full, path too long, etc.
            {
                 Console.WriteLine($"    Error de I/O al guardar {diario.NombreArchivo}: {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"    Error inesperado al descargar {diario.NombreArchivo} de {downloadUrl}: {e.Message}");
            }
            return false;
        }

        private static void GenerarReporte(List<Diario> todosLosDiariosDescargados, Dictionary<int, int> diariosDescargadosPorAnio)
        {
            StringBuilder reporte = new StringBuilder();
            reporte.AppendLine("--- Reporte de Diarios Oficiales Descargados ---");
            reporte.AppendLine($"Cantidad total de diarios descargados exitosamente en esta ejecución: {todosLosDiariosDescargados.Count}");
            reporte.AppendLine("\nDiarios descargados por año en esta ejecución:");

            bool anyDownloaded = false;
            foreach (var kvp in diariosDescargadosPorAnio.OrderBy(k => k.Key))
            {
                if (kvp.Value > 0) 
                {
                    reporte.AppendLine($"  Año {kvp.Key}: {kvp.Value} diarios");
                    anyDownloaded = true;
                }
            }
            
            if (!anyDownloaded)
            {
                 reporte.AppendLine("No se descargaron nuevos diarios en esta ejecución.");
            }

            Console.WriteLine("\n¿Dónde desea imprimir el reporte?");
            Console.WriteLine("1. En pantalla");
            Console.WriteLine("2. En archivo de texto (reporte_diarios.txt)");
            Console.Write("Seleccione una opción (1 o 2): ");
            string? opcion = Console.ReadLine(); // Make opcion nullable

            if (opcion == "2")
            {
                string reportFileName = "reporte_diarios.txt";
                try
                {
                    File.WriteAllText(reportFileName, reporte.ToString());
                    Console.WriteLine($"Reporte guardado en {Path.Combine(Directory.GetCurrentDirectory(), reportFileName)}");
                }
                catch (IOException e)
                {
                    Console.WriteLine($"Error al guardar el reporte en archivo: {e.Message}");
                    Console.WriteLine("\nMostrando reporte en pantalla en su lugar:\n");
                    Console.WriteLine(reporte.ToString());
                }
                catch (Exception e)
                {
                     Console.WriteLine($"Error inesperado al guardar el reporte: {e.Message}");
                    Console.WriteLine("\nMostrando reporte en pantalla en su lugar:\n");
                    Console.WriteLine(reporte.ToString());
                }
            }
            else
            {
                Console.WriteLine("\n--- Contenido del Reporte ---");
                Console.WriteLine(reporte.ToString());
            }
        }
    }

    // Google Drive Helper Class (ensure credentials.json is in the output directory)
    public class EjemploDrive
    {
        static string[] Scopes = { DriveService.Scope.DriveFile };
        static string ApplicationName = "DiariosElSalvadorUploader";
        private const string RootFolderName = "DiarosDeElSalvador"; // Nombre de la carpeta raíz en Drive

        public static async Task UploadFileAsync(string filePath, string year, string month) // month should be "01", "02", etc.
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error en UploadFileAsync: El archivo local no existe en {filePath}");
                return;
            }

            UserCredential credential;
            string credentialsPath = "credentials.json"; // Assumes credentials.json is in the execution directory
            string tokenPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json"); 

            if (!File.Exists(credentialsPath))
            {
                Console.WriteLine($"Error en UploadFileAsync: {credentialsPath} no encontrado.");
                throw new FileNotFoundException($"El archivo de credenciales '{credentialsPath}' no fue encontrado. Asegúrate de que esté en el directorio de ejecución: {AppDomain.CurrentDomain.BaseDirectory}");
            }

            using (var stream =
                new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user", 
                    CancellationToken.None,
                    new FileDataStore(tokenPath, true)); 
                Console.WriteLine("Credential file for Google Drive will be saved to: " + tokenPath);
            }

            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // 1. Get or create the root folder "DiarosDeElSalvador"
            string? rootFolderId = await GetOrCreateFolderAsync(service, RootFolderName, null); // parentId is correctly nullable
            if (string.IsNullOrEmpty(rootFolderId))
            {
                Console.WriteLine($"Google Drive: No se pudo crear o encontrar la carpeta raíz '{RootFolderName}'. Abortando subida para {Path.GetFileName(filePath)}.");
                return;
            }

            // 2. Get or create the year folder inside the root folder
            string? yearFolderId = await GetOrCreateFolderAsync(service, year, rootFolderId);
            if (string.IsNullOrEmpty(yearFolderId)) 
            {
                Console.WriteLine($"Google Drive: No se pudo crear o encontrar la carpeta del año {year} dentro de '{RootFolderName}'. Abortando subida para {Path.GetFileName(filePath)}.");
                return;
            }

            // 3. Get or create the month folder inside the year folder
            string? monthFolderId = await GetOrCreateFolderAsync(service, month, yearFolderId);
            if (string.IsNullOrEmpty(monthFolderId))
            {
                Console.WriteLine($"Google Drive: No se pudo crear o encontrar la carpeta del mes {month} en el año {year}. Abortando subida para {Path.GetFileName(filePath)}.");
                return;
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new[] { monthFolderId }
            };

            FilesResource.CreateMediaUpload request;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read)) 
            {
                request = service.Files.Create(
                    fileMetadata, stream, "application/pdf"); 
                request.Fields = "id, name"; 
                var uploadProgress = await request.UploadAsync();

                if (uploadProgress.Status == Google.Apis.Upload.UploadStatus.Completed)
                {
                    Console.WriteLine($"Google Drive: Archivo '{request.ResponseBody.Name}' (ID: {request.ResponseBody.Id}) subido exitosamente a la carpeta {RootFolderName}/{year}/{month}.");
                }
                else
                {
                    Console.WriteLine($"Google Drive: Falló la subida del archivo '{Path.GetFileName(filePath)}'. Estado: {uploadProgress.Status}, Excepción: {uploadProgress.Exception?.Message}");
                    if (uploadProgress.Exception != null) throw uploadProgress.Exception;
                }
            }
        }

        private static async Task<string?> GetOrCreateFolderAsync(DriveService service, string folderName, string? parentId)
        {
            var fileListRequest = service.Files.List();
            string escapedFolderName = folderName.Replace("'", "\\'");
            string query = $"mimeType='application/vnd.google-apps.folder' and name='{escapedFolderName}' and trashed=false";
            
            if (!string.IsNullOrEmpty(parentId))
            {
                query += $" and '{parentId}' in parents";
            }
            else
            {
                query += " and 'root' in parents";
            }

            fileListRequest.Q = query;
            fileListRequest.Spaces = "drive";
            fileListRequest.Fields = "files(id, name)";
            var files = await fileListRequest.ExecuteAsync();

            if (files.Files != null && files.Files.Count > 0 && files.Files[0].Id != null)
            {
                Console.WriteLine($"Google Drive: Carpeta '{folderName}' encontrada con ID: {files.Files[0].Id}");
                return files.Files[0].Id;
            }
            else
            {
                Console.WriteLine($"Google Drive: Creando carpeta '{folderName}'{(parentId != null ? $" dentro de la carpeta con ID {parentId}" : " en la raíz")}...");
                var folderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = folderName, 
                    MimeType = "application/vnd.google-apps.folder",
                };

                if (!string.IsNullOrEmpty(parentId))
                {
                    folderMetadata.Parents = new[] { parentId };
                }

                var createRequest = service.Files.Create(folderMetadata);
                createRequest.Fields = "id, name";
                var folder = await createRequest.ExecuteAsync();
                if (folder != null && folder.Id != null) 
                {
                    Console.WriteLine($"Google Drive: Carpeta '{folder.Name}' creada con ID: {folder.Id}");
                    return folder.Id;
                }
                else
                {
                    Console.WriteLine($"Google Drive: Falló la creación de la carpeta '{folderName}' o no se retornó ID.");
                    return null;
                }
            }
        }
    }
}
