# 🚀 ZenCloud Backend

<div align="center">

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-316192?style=for-the-badge&logo=postgresql)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker)

**Plataforma de Gestión Multi-Motor de Bases de Datos en la Nube**

[Características](#-características) • [Instalación](#-instalación) • [API](#-api) • [Diagramas](#-diagramas)

</div>

---

## 📖 Descripción

API RESTful construida con **ASP.NET Core 8.0** que permite la gestión centralizada de múltiples motores de bases de datos (SQL y NoSQL) con autenticación JWT, auditoría completa y webhooks.

## ✨ Características

### 🔐 Seguridad
- JWT Bearer Authentication con refresh tokens
- Encriptación AES-256 de contraseñas
- Rate limiting por IP y usuario
- Validación estricta contra SQL Injection

### 💾 Bases de Datos Soportadas
| Motor | Versión | Tipo | Editor SQL | Export |
|-------|---------|------|------------|--------|
| MySQL | 8.0 | SQL | ✅ | ✅ |
| PostgreSQL | 16 | SQL | ✅ | ✅ |
| SQL Server Express | 2022 | SQL | ✅ | ✅ |
| MongoDB | 8 | NoSQL | ❌ | ❌ |
| Redis | 7 | KV Store | ❌ | ❌ |
| Cassandra | 4.1 | NoSQL | ❌ | ❌ |

### 🔍 SQL Editor
- Ejecución segura de consultas
- Historial por usuario
- Listado de tablas y esquemas
- Exportación a .sql

### 📊 Auditoría
- Registro completo de operaciones
- Logs de seguridad
- Notificaciones Discord webhook
- Trazabilidad de cambios

## 🛠 Tecnologías

```
ASP.NET Core 8.0  │  Entity Framework Core 8.0  │  PostgreSQL 18
JWT Authentication  │  Docker & Compose  │  Swagger/OpenAPI
```

**NuGet Packages:**
- `Npgsql.EntityFrameworkCore.PostgreSQL` v8.0.10
- `Microsoft.Data.SqlClient` v5.2.2
- `MySqlConnector` v2.4.0
- `MongoDB.Driver` v3.0.0
- `StackExchange.Redis` v2.8.16
- `AspNetCoreRateLimit` v5.0.0

## 🚀 Instalación

```bash
# Clonar repositorio
git clone https://github.com/nebula-riwi/zencloud-backend.git
cd zencloud-backend

# Configurar variables
cp .env.example .env

# Levantar con Docker
docker compose up -d --build

# Aplicar migraciones
docker exec ZenCloud dotnet ef database update

# API disponible en: http://localhost:8080
# Swagger: http://localhost:8080/swagger
```

## ⚙️ Configuración

### Variables de Entorno Esenciales

```bash
CONNECTION_STRING=Host=some-postgres;Port=5432;Database=ZenCloud;Username=postgres;Password=xxx
JWT_KEY=clave_secreta_minimo_32_caracteres
SMTP_SERVER=smtp.gmail.com
SMTP_USERNAME=email@gmail.com
SMTP_PASSWORD=app_password
WEBHOOK_URL=https://discord.com/api/webhooks/xxx/yyy
ENCRYPTION_KEY=base64_key
MYSQL_ADMIN_PASSWORD=pass123
POSTGRES_ADMIN_PASSWORD=pass123
SQLSERVER_ADMIN_PASSWORD=YourStrong@Passw0rd123
```

## 📚 API

### Endpoints Principales

#### Autenticación
```http
POST   /api/Auth/register          # Registrar
POST   /api/Auth/login             # Login
POST   /api/Auth/refresh           # Refresh token
GET    /api/Auth/me                # Perfil
```

#### Bases de Datos
```http
GET    /api/DatabaseInstance                 # Listar todas
POST   /api/DatabaseInstance                 # Crear nueva
DELETE /api/DatabaseInstance/{id}            # Eliminar
GET    /api/DatabaseInstance/{id}/export     # Exportar SQL
```

#### SQL Editor
```http
GET    /api/databases/{id}/DatabaseManager/tables     # Listar tablas
POST   /api/databases/{id}/DatabaseManager/execute    # Ejecutar query
```

### Ejemplo: Crear Base de Datos

```bash
curl -X POST http://localhost:8080/api/DatabaseInstance \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"engineId":"guid","databaseName":"tienda"}'
```

## 📊 Diagramas

### Arquitectura del Sistema

![Arquitectura ZenCloud](./docs/diagrams/architecture.drawio.png)

La arquitectura sigue el patrón de capas limpia (Clean Architecture):
- **Cliente**: Vue.js 3 con Composition API
- **API**: ASP.NET Core 8.0 con Controllers
- **Servicios**: Lógica de negocio
- **Repositorios**: Acceso a datos
- **Motores de BD**: MySQL, PostgreSQL, SQL Server, MongoDB, Redis, Cassandra

---

### Flujo de Creación de Base de Datos

![Flujo de Creación](./docs/diagrams/create-database-flow.drawio.png)

Proceso completo desde la solicitud del usuario hasta la notificación:
1. Autenticación JWT
2. Validación de datos
3. Creación física de BD
4. Creación de usuario y permisos
5. Encriptación de contraseñas (AES-256)
6. Registro en sistema
7. Auditoría y webhooks

---

### Casos de Uso

![Casos de Uso](./docs/diagrams/use-cases.drawio.png)

**Actores:**
- **Usuario**: Gestión básica de bases de datos, SQL Editor, exportación
- **Administrador**: Gestión de usuarios, auditoría, configuración de motores

**Funcionalidades principales:**
- Crear, listar, exportar bases de datos
- Ejecutar consultas SQL
- Ver historial de queries
- Gestión de credenciales

---

### Diagrama de Clases

![Diagrama de Clases](./docs/diagrams/class-diagram.drawio.png)

**Entidades principales:**
- `User`: Usuarios del sistema
- `DatabaseInstance`: Instancias de bases de datos
- `DatabaseEngine`: Motores disponibles
- `AuditLog`: Logs de auditoría
- `DatabaseQueryHistory`: Historial de consultas

**Servicios:**
- `IAuthService`: Autenticación y autorización
- `IDatabaseInstanceService`: Gestión de instancias
- `IDatabaseManagementService`: Ejecución de queries
- `IEncryptionService`: Encriptación de datos

---

📁 **Archivos editables**: Los diagramas fuente en formato XML están en [`/docs/diagrams`](./docs/diagrams) y pueden editarse en [draw.io](https://app.diagrams.net)

## 🧪 Testing

```bash
# Ejecutar tests unitarios
dotnet test

# Con cobertura
dotnet test /p:CollectCoverage=true
```

## 🐳 Docker

### Recursos (Servidor 8GB RAM)

```yaml
Backend: 2GB (3 CPUs)
PostgreSQL Main: 1GB
SQL Server: 2GB
MySQL: 1GB
PostgreSQL: 1GB
MongoDB: 1GB
Cassandra: 1.5GB
Redis: 512MB
```

## 🔒 Seguridad

- ✅ HTTPS obligatorio en producción
- ✅ Rate limiting: 100 req/min por IP
- ✅ CORS configurado
- ✅ Contraseñas encriptadas (AES-256)
- ✅ JWT con expiración configurable
- ✅ Validación de entrada estricta

## 📝 Logs y Auditoría

```bash
# Ver logs del backend
docker logs ZenCloud -f

# Ver logs de SQL Server
docker logs sqlserver-ZenDb --tail 50

# Ver logs de base de datos principal
docker logs some-postgres -f
```

## 🚀 Despliegue Producción

```bash
# En el servidor
cd /var/www/zencloud-backend
git pull origin main
docker compose down
docker compose up -d --build

# Verificar
docker ps
docker logs ZenCloud --tail 50
```

## 🤝 Contribución

1. Fork el proyecto
2. Crea una rama (`git checkout -b feature/nueva-funcionalidad`)
3. Commit cambios (`git commit -m 'feat: nueva funcionalidad'`)
4. Push a la rama (`git push origin feature/nueva-funcionalidad`)
5. Abre un Pull Request

## 📄 Licencia

Este proyecto está bajo la Licencia MIT - ver [LICENSE](LICENSE) para detalles.

## 👥 Equipo

Desarrollado por **[Nebula-RIWI](https://github.com/nebula-riwi)**

---

<div align="center">
  <strong>⭐ Si te gusta el proyecto, dale una estrella!</strong>
</div>
