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
        public string? FechaInicio { get; set; }

        [JsonPropertyName("FechaInexacta")]
        public string? FechaInexacta { get; set; }

        [JsonPropertyName("NombreArchivo")]
        public string? NombreArchivo { get; set; }
    }

    public class ResumenDiarios
    {
        public List<Diario> TodosLosDiarios { get; set; } = new();
        public Dictionary<int, Dictionary<int, List<Diario>>> DiariosPorAnioYMes { get; set; } = new();
        public int TotalDiarios => TodosLosDiarios.Count;
        public int TotalAniosConDiarios => DiariosPorAnioYMes.Count;
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string BaseUrl = "https://www.diariooficial.gob.sv";
        private const string MesesDisponiblesApi = "/api/v1/meses-disponibles";
        private const string DiariosDisponiblesApi = "/api/v1/diarios-disponibles";
        private const string DiarioDownloadBaseUrl = "https://www.diariooficial.gob.sv/seleccion/";
        private const string OutputDirectory = "DiariosOficiales";
        private const string CredentialsPath = "credentials.json";
        private const string TokenPath = "token.json";
        
        private static DriveService? driveService;
        private static string? carpetaRaizDriveId;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== DESCARGADOR DE DIARIOS OFICIALES DE EL SALVADOR ===\n");
            
            Console.WriteLine("Analizando diarios disponibles...");
            var resumen = await DescubrirDiariosAsync();
            MostrarYGuardarReporte(resumen);
            
            await Task.Delay(2000);
            
            // Inicializar Google Drive solo al momento de subir archivos
            Console.WriteLine("\n🔗 Inicializando conexión con Google Drive...");
            if (!await InicializarGoogleDriveAsync())
            {
                Console.WriteLine("Error: No se pudo conectar a Google Drive. Verifica tu archivo credentials.json");
                Console.WriteLine("Solo se generará el reporte local.");
                Console.WriteLine("\nProceso completado. Presione cualquier tecla para salir.");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("Google Drive conectado exitosamente\n");
            
            Console.WriteLine("⬇Iniciando descarga y subida a Google Drive");
            await DescargarYSubirDiariosAsync(resumen);
            
            Console.WriteLine("\nProceso completado. Presione cualquier tecla para salir.");
            Console.ReadKey();
        }

        private static async Task<ResumenDiarios> DescubrirDiariosAsync()
        {
            var resumen = new ResumenDiarios();
            int startYear = 1847;
            int endYear = 2025;

            for (int year = startYear; year <= endYear; year++)
            {
                Console.Write($"Analizando año {year} --> ");
                
                var meses = await GetAvailableMonthsAsync(year);
                if (!meses.Any())
                {
                    Console.WriteLine("sin datos");
                    continue;
                }

                var diariosDelAnio = new Dictionary<int, List<Diario>>();
                int totalDiariosAnio = 0;
                
                foreach (var mesObj in meses)
                {
                    if (!int.TryParse(mesObj.Month, out int month)) continue;
                    
                    var diariosDelMes = await GetAvailableDiariosAsync(year, month);
                    if (diariosDelMes.Any())
                    {
                        diariosDelAnio[month] = diariosDelMes;
                        resumen.TodosLosDiarios.AddRange(diariosDelMes);
                        totalDiariosAnio += diariosDelMes.Count;
                    }
                }

                if (totalDiariosAnio > 0)
                {
                    resumen.DiariosPorAnioYMes[year] = diariosDelAnio;
                    Console.WriteLine($"{totalDiariosAnio} diarios encontrados");
                }
                else
                {
                    Console.WriteLine("sin diarios");
                }

                await Task.Delay(100); // Cortesía al servidor
            }

            return resumen;
        }

        private static void MostrarYGuardarReporte(ResumenDiarios resumen)
        {
            var reporte = GenerarReporteSimplificado(resumen);
            
            // Mostrar en pantalla
            Console.WriteLine(reporte);
            
            // Guardar automáticamente
            try
            {
                File.WriteAllText("Reporte.txt", reporte, Encoding.UTF8);
                Console.WriteLine($"Reporte guardado automáticamente en: {Path.GetFullPath("Reporte.txt")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar reporte: {ex.Message}");
            }
        }

        private static string GenerarReporteSimplificado(ResumenDiarios resumen)
        {
            var sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine("REPORTE DE DIARIOS OFICIALES DISPONIBLES");
            sb.AppendLine("============================================================");
            sb.AppendLine($"Total de diarios encontrados: {resumen.TotalDiarios:N0}");
            sb.AppendLine($"Años con diarios disponibles: {resumen.TotalAniosConDiarios}");
            
            if (resumen.DiariosPorAnioYMes.Any())
            {
                var anioMinimo = resumen.DiariosPorAnioYMes.Keys.Min();
                var anioMaximo = resumen.DiariosPorAnioYMes.Keys.Max();
                sb.AppendLine($"Período: {anioMinimo} - {anioMaximo}");
            }
            
            sb.AppendLine("============================================================");
            
            return sb.ToString();
        }

        private static async Task<bool> InicializarGoogleDriveAsync()
        {
            try
            {
                // Verificar que existe el archivo de credenciales
                if (!File.Exists(CredentialsPath))
                {
                    Console.WriteLine($"Error: No se encontró el archivo {CredentialsPath}");
                    return false;
                }

                UserCredential credential;
                using (var stream = new FileStream(CredentialsPath, FileMode.Open, FileAccess.Read))
                {
                    string[] scopes = { DriveService.Scope.DriveFile };
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(TokenPath, true));
                }

                // Crear el servicio de Drive
                driveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Descargador Diarios El Salvador",
                });

                // Crear o encontrar la carpeta raíz
                carpetaRaizDriveId = await CrearOEncontrarCarpetaAsync("Diarios Oficiales El Salvador", null);
                
                return driveService != null && !string.IsNullOrEmpty(carpetaRaizDriveId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al inicializar Google Drive: {ex.Message}");
                return false;
            }
        }

        private static async Task<string?> CrearOEncontrarCarpetaAsync(string nombreCarpeta, string? carpetaPadreId)
        {
            try
            {
                // Buscar si la carpeta ya existe
                var listRequest = driveService!.Files.List();
                listRequest.Q = $"name='{nombreCarpeta}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
                if (!string.IsNullOrEmpty(carpetaPadreId))
                {
                    listRequest.Q += $" and '{carpetaPadreId}' in parents";
                }

                var existingFiles = await listRequest.ExecuteAsync();
                if (existingFiles.Files.Count > 0)
                {
                    return existingFiles.Files[0].Id;
                }

                // Crear la carpeta si no existe
                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = nombreCarpeta,
                    MimeType = "application/vnd.google-apps.folder"
                };

                if (!string.IsNullOrEmpty(carpetaPadreId))
                {
                    fileMetadata.Parents = new List<string> { carpetaPadreId };
                }

                var request = driveService!.Files.Create(fileMetadata);
                var file = await request.ExecuteAsync();
                return file.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al crear/encontrar carpeta '{nombreCarpeta}': {ex.Message}");
                return null;
            }
        }

        private static async Task<bool> SubirArchivoADriveAsync(string rutaArchivoLocal, string nombreArchivo, string carpetaDestinoId)
        {
            try
            {
                // Verificar si el archivo ya existe en Drive
                var listRequest = driveService!.Files.List();
                listRequest.Q = $"name='{nombreArchivo}' and '{carpetaDestinoId}' in parents and trashed=false";
                var existingFiles = await listRequest.ExecuteAsync();
                
                if (existingFiles.Files.Count > 0)
                {
                    return true; // Ya existe
                }

                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = nombreArchivo,
                    Parents = new List<string> { carpetaDestinoId }
                };

                using var stream = new FileStream(rutaArchivoLocal, FileMode.Open, FileAccess.Read);
                var request = driveService!.Files.Create(fileMetadata, stream, "application/pdf");
                request.Fields = "id";
                
                var file = await request.UploadAsync();
                return file.Status == Google.Apis.Upload.UploadStatus.Completed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al subir '{nombreArchivo}': {ex.Message}");
                return false;
            }
        }
        private static async Task DescargarYSubirDiariosAsync(ResumenDiarios resumen)
        {
            Directory.CreateDirectory(OutputDirectory);
            
            int descargados = 0;
            int subidos = 0;
            int errores = 0;
            int total = resumen.TotalDiarios;

            Console.WriteLine($"Se descargaran {total:N0} diarios, esto tardará bastante tiempo\n");

            foreach (var anioDiarios in resumen.DiariosPorAnioYMes.OrderBy(x => x.Key))
            {
                int anio = anioDiarios.Key;
                var mesesDiarios = anioDiarios.Value;

                Console.WriteLine($"Procesando año {anio}");

                // Crear directorio local del año
                var directorioAnio = Path.Combine(OutputDirectory, anio.ToString());
                Directory.CreateDirectory(directorioAnio);

                // Crear carpeta del año en Drive
                var carpetaAnioId = await CrearOEncontrarCarpetaAsync(anio.ToString(), carpetaRaizDriveId);
                if (string.IsNullOrEmpty(carpetaAnioId))
                {
                    Console.WriteLine($"Error: No se pudo crear carpeta del año {anio} en Drive");
                    continue;
                }

                foreach (var mesDiarios in mesesDiarios.OrderBy(x => x.Key))
                {
                    int mes = mesDiarios.Key;
                    var diarios = mesDiarios.Value;

                    Console.WriteLine($"  Mes {mes:D2} ({diarios.Count} diarios)");

                    // Crear directorio local del mes
                    var directorioMes = Path.Combine(directorioAnio, $"Mes_{mes:D2}");
                    Directory.CreateDirectory(directorioMes);

                    // Crear carpeta del mes en Drive
                    var carpetaMesId = await CrearOEncontrarCarpetaAsync($"Mes_{mes:D2}", carpetaAnioId);
                    if (string.IsNullOrEmpty(carpetaMesId))
                    {
                        Console.WriteLine($"    Error: No se pudo crear carpeta del mes {mes} en Drive");
                        continue;
                    }

                    foreach (var diario in diarios)
                    {
                        var progreso = $"[{descargados + errores + 1}/{total}]";
                        Console.Write($"    {progreso} {diario.NombreArchivo}");

                        try
                        {
                            // Descargar archivo
                            var rutaArchivoLocal = await DescargarDiarioAsync(diario, directorioMes);
                            if (!string.IsNullOrEmpty(rutaArchivoLocal))
                            {
                                descargados++;
                                Console.Write(" Descargado");

                                // Subir a Google Drive
                                bool subidoExitosamente = await SubirArchivoADriveAsync(rutaArchivoLocal, diario.NombreArchivo!, carpetaMesId);
                                if (subidoExitosamente)
                                {
                                    subidos++;
                                    Console.WriteLine(" Subido a Drive");
                                    try
                                    {
                                        // Eliminar archivo local después de subir
                                        File.Delete(rutaArchivoLocal);
                                    }
                                    catch
                                    {
                                        Console.WriteLine(" (No se pudo eliminar archivo local)");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine(" Error al subir a Drive");
                                }
                            }
                            else
                            {
                                errores++;
                                Console.WriteLine(" Error al descargar");
                            }
                        }
                        catch (Exception ex)
                        {
                            errores++;
                            Console.WriteLine($" Error: {ex.Message}");
                        }
                        await Task.Delay(500); 
                    }
                }
            }
            Console.WriteLine($"\nRESUMEN FINAL:");
            Console.WriteLine($"Descargados exitosamente: {descargados:N0}");
            Console.WriteLine($"Subidos a Google Drive: {subidos:N0}");
            Console.WriteLine($"Errores: {errores:N0}");
            Console.WriteLine($"Carpeta en Google Drive: 'Diarios Oficiales El Salvador'");
        }
        private static async Task<string?> DescargarDiarioAsync(Diario diario, string directorioDestino)
        {
            if (string.IsNullOrEmpty(diario.Id) || string.IsNullOrEmpty(diario.NombreArchivo))
                return null;

            var url = $"{DiarioDownloadBaseUrl}{diario.Id}";
            var rutaArchivo = Path.Combine(directorioDestino, diario.NombreArchivo);

            if (File.Exists(rutaArchivo))
                return rutaArchivo; // Ya existe

            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var fs = new FileStream(rutaArchivo, FileMode.Create);
                    await response.Content.CopyToAsync(fs);
                    return rutaArchivo;
                }
            }
            catch (HttpRequestException)
            {
                // Error de conexión - se maneja en el nivel superior
            }
            catch (IOException)
            {
                // Error de E/S - se maneja en el nivel superior
            }

            return null;
        }

        private static async Task<List<MesDisponible>> GetAvailableMonthsAsync(int year)
        {
            try
            {
                var payload = new { year = year.ToString() };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                
                var response = await httpClient.PostAsync(BaseUrl + MesesDisponiblesApi, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var meses = await response.Content.ReadFromJsonAsync<List<MesDisponible>>();
                    return meses ?? new List<MesDisponible>();
                }
            }
            catch (Exception)
            {
                // Se maneja silenciosamente para no interrumpir el proceso
            }
            
            return new List<MesDisponible>();
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
                
                var response = await httpClient.PostAsync(BaseUrl + DiariosDisponiblesApi, content);

                if (response.IsSuccessStatusCode)
                {
                    var diarios = await response.Content.ReadFromJsonAsync<List<Diario>>();
                    return diarios ?? new List<Diario>();
                }
            }
            catch (Exception)
            {
                // Se maneja silenciosamente para no interrumpir el proceso
            }
            
            return new List<Diario>();
        }
    }
}