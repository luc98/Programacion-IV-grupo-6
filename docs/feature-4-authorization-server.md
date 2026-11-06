# Documentación: Authorization Server (Seguridad centralizada)

## 1. Justificación del Componente (3rd Party Identity Provider)
De acuerdo a los lineamientos arquitectónicos del Trabajo Práctico, el sistema adopta un modelo de seguridad centralizado utilizando **Keycloak** como proveedor de identidad (IdP) de terceros. 

El rol fundamental de este componente es abstraer y unificar toda la complejidad de la autenticación y autorización, separándola de la lógica de negocio de los microservicios. 
**Beneficios de esta decisión arquitectónica:**
* **Estandarización:** Implementa de forma nativa y certificada los protocolos OAuth 2.0 y OpenID Connect (OIDC).
* **Identity Brokering:** Facilita la delegación de identidad, permitiendo integrar Google Sign-In para usuarios finales sin programar validaciones criptográficas a mano, evitando vulnerabilidades de seguridad.
* **Autenticación Desatendida:** Provee un mecanismo robusto para que las APIs operen de forma segura (Machine-to-Machine) mediante la emisión y validación de tokens JWT.

## 2. Orquestación e Infraestructura
El Authorization Server se ejecuta en el entorno local a través de Docker Compose, compartiendo la red interna del ecosistema para facilitar la conexión de los microservicios.

* **Motor:** `quay.io/keycloak/keycloak:latest` (Modo desarrollo activado vía `start-dev`).
* **Persistencia:** Base de datos relacional PostgreSQL 15 (`keycloak-db`).
* **Red:** `booking_network` (Permite que las APIs en Docker apunten internamente al host `keycloak:8080`).
* **Exposición:** Puerto `8080` mapeado al host (`localhost:8080`).
* **Respaldo:** La configuración se encuentra exportada en el archivo `realm-export.json` en la raíz de la infraestructura.

## 3. Configuración del Entorno (Realm)
Todo el sistema opera encapsulado dentro del espacio de trabajo denominado **`reservas-realm`**. Los microservicios de las distintas duplas deben apuntar a este Realm para sus operaciones de validación.

### 3.1. Clientes Confidenciales (Microservicios Backend)
Diseñados para la comunicación Máquina a Máquina (M2M) sin interacción humana.
* **Clientes registrados:** `booking-api`, `payment-api`, `notifications-api`, `availability-api`.
* **Flujo utilizado:** *Client Credentials Grant* (`Service accounts roles` = ON, `Standard flow` = OFF).
* **Configuración de seguridad:** Autenticación de cliente activada, requiriendo validación mediante `Client ID` y `Client Secret`.
* **Audience Mapping:** El token emitido para `availability-api` inyecta a `booking-api` en el claim `aud`, garantizando mediante introspección y validación local que el token es consumido por el destinatario legítimo.

### 3.2. Cliente Público (Frontend)
Diseñado para la interacción con el usuario humano y el navegador web.
* **Cliente registrado:** `availability-site`.
* **Flujo utilizado:** *Authorization Code Flow* (`Standard flow` = ON, `Service accounts roles` = OFF).
* **Configuración de seguridad:** Cliente público (sin *Client Secret*). Se aplican restricciones estrictas sobre los *Valid redirect URIs* para prevenir redirecciones maliciosas post-login.
* **Identity Brokering:** Vinculado con la consola de Google Cloud para ofrecer inicio de sesión con cuentas `@gmail.com` bajo el entorno de pruebas.

## 4. Endpoints de Integración
Las APIs del sistema deben configurarse con los siguientes endpoints en sus variables de entorno (`appsettings.json`):

**A. Para validación interna y configuración base (Issuer/Authority):**
* Local (Visual Studio): `http://localhost:8080/realms/reservas-realm`
* Docker (Red interna): `http://keycloak:8080/realms/reservas-realm`

**B. Para solicitar tokens M2M (HTTP POST):**
* Local (Visual Studio): `http://localhost:8080/realms/reservas-realm/protocol/openid-connect/token`
* Docker (Red interna): `http://keycloak:8080/realms/reservas-realm/protocol/openid-connect/token`
* *Payload Requerido:* `client_id`, `client_secret`, `grant_type=client_credentials` formato `application/x-www-form-urlencoded`.

**C. Para redirección de inicio de sesión de usuarios (Frontend - HTTP GET):**
* URL exclusiva de navegador: `http://localhost:8080/realms/reservas-realm/protocol/openid-connect/auth`
* *Query Params:* `client_id=availability-site`, `response_type=code`, `redirect_uri=...`, `scope=openid profile email`

## 5. Instrucciones de despliegue para el equipo
1. Ejecutar `docker-compose up -d` en la carpeta de infraestructura.
2. Ingresar a `http://localhost:8080` (Credenciales: `admin` / `admin`).
3. Crear un Realm nuevo.
4. Arrastrar el archivo `realm-export.json` al cuadro de *Resource file* e importar. El entorno `reservas-realm` quedará 100% operativo.