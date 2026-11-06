# Registro de Memoria de Prompts y Actividades

**Integrante:** Lucas Nan

**Dupla:** 4 (Notificaciones, Seguridad, Trazabilidad y Entrega Integral)

**Proyecto:** Sistema de Reservas - Programación IV (UTN)

## 1. Resumen de Responsabilidades y Ejecución

Como miembro de la **Dupla 4**, mis responsabilidades principales en este trabajo práctico incluyeron:

1. **Infraestructura de Seguridad (Authorization Server):** Despliegue y configuración de Keycloak mediante Docker Compose, habilitando el aprovisionamiento de un entorno aislado (`booking-realm` / `reservas-realm`), clientes confidenciales M2M (Client Credentials) para la comunicación segura entre microservicios, y la integración de un proveedor de identidad de terceros (Google Sign-In) mediante *Identity Brokering*.

2. **Orquestación de Contenedores:** Consolidación de los distintos archivos `docker-compose.yml` del equipo en un único orquestador centralizado, gestionando redes virtuales aisladas (`booking_network`), mapeo seguro de puertos externos sin colisiones y volúmenes persistentes independientes para bases de datos relacionales (PostgreSQL / SQL Server).

3. **Documentación de Seguridad y Arquitectura:** Elaboración de la documentación técnica y de los diagramas nativos de arquitectura (.drawio) correspondientes al servidor de autorización y el esquema general de red.

4. **Trazabilidad Transversal y Auditoría:** Diseño de la estrategia de observabilidad basada en logs estructurados (Serilog) y la propagación de un identificador de traza único (`TraceId` / `CorrelationId`) a través de las cabeceras HTTP/gRPC y metadatos de Azure Service Bus.

## 2. Historial Consolidado de Interacciones y Prompts

A continuación se enlistan todas las consultas técnicas, decisiones arquitectónicas e interacciones registradas a lo largo de las sesiones de desarrollo de este proyecto:

* **Configuración del Servidor de Autorización y Keycloak:**
  * Elección y justificación del uso de Keycloak como proveedor de identidad de terceros frente a herramientas locales como `dotnet user-jwts`, destacando su compatibilidad con flujos OAuth2/OIDC, *Identity Brokering* para Google Sign-In y los estándares corporativos.
  * Definición de los clientes confidenciales con el flujo *Client Credentials* para la comunicación M2M de los microservicios (`booking-api`, `payment-api`, `notifications-api`, `availability-api`), y del cliente público (`availability-site`) para el flujo de código de autorización del frontend.
  * Configuración de la integración con Google Cloud Platform (GCP) creando un proyecto bajo la pantalla de consentimiento de OAuth externa y vinculando las *Redirect URIs*.
  * Creación y gestión de los clients secrets, la inyección del claim `aud` para `booking-api` mediante el *Audience Mapper*, y la exportación e importación del archivo de respaldo `realm-export.json`.

* **Orquestación de Infraestructura y Docker Compose:**
  * Configuración del entorno de virtualización en Windows mediante WSL 2 y la habilitación de la característica de virtualización por hardware (Intel VT-x / AMD-V / SVM Mode) en la BIOS/UEFI de la placa madre ASUS.
  * Creación y unificación de los archivos `docker-compose.yml` para consolidar Keycloak, PostgreSQL y las bases de datos de los microservicios bajo la red virtual `booking_network` sin colisiones de puertos ni sobreescritura de volúmenes.
  * Resolución de errores de compilación y subida de cambios en Git, incluyendo el manejo de ramas divergentes (`git merge --abort`), la limpieza del caché local de Visual Studio (`.vs/`) y la correcta incorporación del archivo `.gitignore`.

* **Integración de Seguridad y Endpoints en .NET:**
  * Documentación de los endpoints estándar de OAuth2 consumidos por las APIs:
    * `/protocol/openid-connect/auth` (Método GET con *Query Parameters* para redirección de usuarios).
    * `/protocol/openid-connect/token` (Método POST con contenido `application/x-www-form-urlencoded` y payload con `client_id`, `client_secret` y `grant_type=client_credentials`).
  * Estructuración del cuerpo de las peticiones mediante la clase `FormUrlEncodedContent` en C# para solicitar tokens M2M y su posterior inyección como *Bearer Token* en cabeceras HTTP/gRPC.
  * Análisis del flujo de autenticación para el *Availability Site*, diferenciando entre el cliente frontend público (que no requiere secreto) y el backend dedicado que maneja llamadas internas.

## 3. Conclusiones y Perfil Profesional Adquirido

El desarrollo de este trabajo práctico permitió consolidar conocimientos fundamentales en el diseño de **sistemas distribuidos y arquitecturas nativas de nube (Cloud-Native)**. A lo largo del camino se aprendió a:
* Desacoplar dominios mediante microservicios independientes con persistencia propia y descentralizada.
* Garantizar la seguridad transaccional y la autenticación centralizada sin comprometer la escalabilidad.
* Resolver problemas reales de infraestructura, redes virtuales en contenedores y control de concurrencia.

El perfil obtenido a partir de esta experiencia se alinea con el de un **Backend / Cloud-Native Developer**, preparado para diseñar, asegurar y orquestar ecosistemas de software robustos, resilientes y tolerantes a fallos.