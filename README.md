# DiariosElSalvador

Este proyecto parece ser una aplicación de consola .NET/C#.

## Descripción

Este proyecto es una aplicación de consola en C# diseñada para descargar Diarios Oficiales de El Salvador desde el sitio web www.diariooficial.gob.sv y subirlos automáticamente a una carpeta específica en Google Drive. La aplicación descubre los diarios disponibles por año y mes, genera un reporte simplificado y luego procede a descargar y subir los archivos PDF a Google Drive, organizándolos en carpetas por año y mes.


## Cómo ejecutar

1. Asegúrate de tener el SDK de .NET instalado.
2. Abre una terminal en el directorio raíz del proyecto.
3. Ejecuta el siguiente comando para construir el proyecto:
   ```bash
   dotnet build
   ```
4. Ejecuta el siguiente comando para correr la aplicación:
   ```bash
   dotnet run
   ```

## Requisitos

Para ejecutar la aplicación, necesitas un archivo `credenciales.json` en el directorio raíz del proyecto. Este archivo no se incluye en el repositorio por razones de seguridad (está ignorado en `.gitignore`).

Además, deberás iniciar sesión con tu cuenta de Google y otorgar los permisos necesarios para que la aplicación acceda a los servicios de Google Drive. Si encuentras un error de "Acceso bloqueado" o "Error 403: access_denied", es posible que necesites contactar al desarrollador para que tu cuenta sea añadida como verificador de prueba, ya que la aplicación no ha completado el proceso de verificación de Google.

## Estructura del proyecto

- `DiariosElSalvador.csproj`: Archivo de proyecto C#.
- `DiariosElSalvador.sln`: Archivo de solución de Visual Studio.
- `Program.cs`: Punto de entrada principal de la aplicación.

(Agregar más secciones según sea necesario, como Contribución, Licencia, etc.)