# Proyecto: **CrudCloudDb Platform**  ->  "CCD"

## Descripción General
El presente proyecto tiene como objetivo el desarrollo de una plataforma web tipo Clever Cloud, enfocada en la gestión automatizada de bases de datos en la nube. A través de esta plataforma, los usuarios podrán crear, administrar, escalar y eliminar instancias de bases de datos pertenecientes a distintos motores (MySQL, PostgreSQL, MongoDB, SQL Server, Redis y Cassandra) de manera centralizada, intuitiva y segura

La plataforma estará compuesta por una interfaz web desarrollada en Vue.js y un backend basado en ASP.NET Core Web API, comunicados mediante servicios REST y protegidos con autenticación JWT (JSON Web Token).

Los usuarios podrán registrarse gratuitamente, accediendo por defecto a un plan gratuito que les permitirá crear hasta dos bases de datos por motor. Posteriormente, podrán ampliar sus capacidades mediante un sistema de membresías o planes pagos, administrado a través de Mercado Pago como pasarela transaccional. Cada plan definirá límites en la cantidad de bases de datos que el usuario puede mantener activas, ajustando dinámicamente los permisos y recursos asignados.

La plataforma deberá garantizar la seguridad, aislamiento y correcta gestión de permisos entre usuarios, evitando accesos cruzados o modificaciones indebidas en las bases de datos de otros clientes. Para cada instancia creada, el sistema deberá generar automáticamente las credenciales de conexión (usuario, contraseña, puerto, nombre de base de datos) y enviarlas al usuario mediante correo electrónico.

---

## ⚙️ Funcionalidad del Sistema
Al registrarse, el usuario accede automáticamente al **plan gratuito**, que permite crear hasta **dos bases de datos por cada motor**:

- **Plan gratuito:** hasta 2 bases de datos por motor.  
- **Plan intermedio:** hasta 5 bases de datos por motor — 💰 $5.000 COP/mes.  
- **Plan avanzado:** hasta 10 bases de datos por motor — 💰 $10.000 COP/mes.  

El sistema debe garantizar que cada base de datos creada tenga su propio usuario, contraseña, puerto y permisos correctamente aislados, evitando conflictos entre usuarios.

---

## 🧱 Tecnologías a Utilizar

| Componente | Tecnología |
|-------------|-------------|
| **Frontend** | Vue.js |
| **Backend** | ASP.NET Core Web API |
| **Autenticación** | JWT (JSON Web Token) |
| **Pasarela de pagos** | Mercado Pago |
| **Correos electrónicos** | Servicio SMTP configurable (SendGrid, Mailgun, etc.) |
| **Notificaciones externas** | Webhooks |

---

## 🧩 Funcionalidades Principales

### 1. Registro y Autenticación
- Creación de cuenta y verificación por correo electrónico.  
- Inicio de sesión mediante JWT.  
- Recuperación de contraseña por correo.  

### 2. Gestión de Planes y Membresías
- Plan gratuito asignado automáticamente al registrarse.  
- Actualización de plan mediante integración con **Mercado Pago**.  
- Control de cuotas por cantidad de bases de datos y motor.  

### 3. Creación y Administración de Bases de Datos
- Selección del motor (MySQL, PostgreSQL, MongoDB, SQL Server, Redis, Cassandra).  
- Generación automática de credenciales: usuario, puerto, base de datos, contraseña.  
- Visualización controlada de credenciales (solo la primera vez).  
- Eliminación o rotación de credenciales bajo demanda.  
- Envío automático de correos al crear o eliminar bases de datos.  

### 4. Facturación y Pagos
- Creación de suscripciones y cobros mensuales mediante **Mercado Pago**.  
- Validación de pagos mediante Mercado Pago.  
- Actualización automática del plan tras confirmación del pago.  

### 5. Notificaciones por Correo
- Al crear una cuenta.  
- Al crear una base de datos (con credenciales de acceso).  
- Al eliminar una base de datos.  
- Al cambiar o renovar plan.  

### 6. Webhooks
**a. Notificaciones por acciones de usuario:**
- Creación de cuenta.  
- Creación de base de datos.

**b. Notificaciones por errores en producción:**
- Envío automático de información de errores (excepciones capturadas en `catch`) al equipo de desarrollo.  

### 7. Panel de Control (Frontend)
- Visualización del plan actual y cuota disponible.  
- Listado de bases de datos creadas con estado y motor.  
- Creación y eliminación de bases de datos.  
- Gestión de facturación y suscripciones.  
- Configuración de webhooks personales.  

---

## Requisitos de Seguridad
- Cada base de datos debe tener **usuarios y permisos independientes**.  
- Comunicación cliente-servidor mediante **HTTPS**.  
- Contraseñas cifradas, nunca almacenadas en texto plano.  
- Manejo de errores y logs con auditoría de eventos.  

---

## Requisitos de Comunicación y Reportes
- Registro de acciones importantes (auditoría del sistema).  
- Reporte automático de errores en producción vía webhook.  
- Registro del estado de envío de correos y notificaciones (“enviado” / “no enviado”).  

---

## Resultados Esperados
El prototipo debe permitir:
- Crear una cuenta y acceder a la plataforma.  
- Crear bases de datos en al menos **dos motores distintos**.  
- Cambiar de plan mediante Mercado Pago (modo sandbox).  
- Recibir correos automáticos de confirmación.  
- Generar notificaciones vía webhooks.  
- Usar un panel web limpio, moderno e intuitivo.  

---

## 📦 Entregables
1. **Documento de arquitectura:** diagramas, flujos y dependencias.  
2. **Backend:** API en ASP.NET Core con JWT y endpoints funcionales.  
3. **Frontend:** interfaz en Vue.js con rutas, componentes y estilos.  
4. **Integración con Mercado Pago:** funcional en produccion.  
5. **Sistema de correos y webhooks:** operativo y documentado.  
6. **Video demostrativo:** mostrando el flujo principal.  
7. **Repositorio del proyecto:** con documentación y README explicativo.  

---

## Recomendaciones para el Desarrollo
- Usar control de versiones (Git/GitHub).  
- Emplear buenas prácticas de arquitectura en capas.  
- Crear entornos separados: desarrollo, pruebas y producción.  
- Validar todas las entradas del usuario.  
- Implementar logs y auditoría en el backend.  

---

## Competencias a Desarrollar
- Diseño e implementación de APIs seguras con JWT.  
- Integración de pasarelas de pago (Mercado Pago).  
- Gestión y automatización de recursos en servidores.  
- Diseño de interfaces reactivas con Vue.js.  
- Implementación de notificaciones y manejo de excepciones.  
- Trabajo colaborativo y uso de Git.  

---

## 📊 Criterios de Evaluación
| Criterio | Descripción | Peso |
|-----------|-------------|------|
| **Arquitectura del sistema** | Diseño estructurado, separación de capas, uso de buenas prácticas. | 20% |
| **Funcionalidad backend** | Creación, autenticación, manejo de bases de datos, webhooks. | 25% |
| **Interfaz frontend** | Claridad, usabilidad, experiencia de usuario, diseño. | 20% |
| **Integraciones externas** | Mercado Pago, correos y webhooks funcionales. | 15% |
| **Seguridad y manejo de errores** | Implementación de JWT, cifrado, control de excepciones. | 10% |
| **Documentación y presentación** | README, diagramas, video demostrativo y repositorio. | 10% |

---

> **Nota:** Este proyecto simula una plataforma real de gestión de bases de datos en la nube. Se recomienda trabajar en equipos, dividir responsabilidades y aplicar prácticas de desarrollo ágil.

---


Todo el proyecto debe estar con un subdominio 
```
service.voyager.andrescortes.dev -> Backend
voyager.andrescortes.dev -> Frontend
```