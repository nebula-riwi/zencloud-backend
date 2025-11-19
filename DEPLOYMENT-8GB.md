# 🚀 Guía de Despliegue para Servidor 8GB RAM

## 📊 Distribución de Recursos Optimizada

| Servicio | CPU | RAM | Descripción |
|----------|-----|-----|-------------|
| **Backend (ZenCloud)** | 3.0 / 1.0 | 2GB / 1GB | Aplicación principal ASP.NET |
| **PostgreSQL Principal** | 2.0 / 0.5 | 1GB / 512MB | Base de datos del sistema |
| **SQL Server Express** | 2.0 / 1.0 | 2GB / 1.5GB | Motor relacional (FREE) |
| **Cassandra** | 2.0 / 0.5 | 1.5GB / 768MB | Motor NoSQL |
| **MySQL** | 1.5 / 0.5 | 1GB / 512MB | Motor relacional usuarios |
| **PostgreSQL Usuarios** | 1.5 / 0.5 | 1GB / 512MB | Motor relacional usuarios |
| **MongoDB** | 1.5 / 0.5 | 1GB / 512MB | Motor NoSQL usuarios |
| **n8n** | 1.5 / 0.5 | 1GB / 512MB | Automatización |
| **Redis** | 1.0 / 0.25 | 512MB / 256MB | Cache y KV store |

**Total:** ~10.5GB límite / ~6GB reservado
- Docker gestiona dinámicamente la memoria
- El swap manejará picos temporales

---

## 🛠️ PASO 1: CONFIGURAR SWAP (4GB)

```bash
# Conectar al servidor
ssh root@168.119.182.243

# Copiar script de swap
cd /var/www/zencloud-backend

# Ejecutar configuración
sudo bash configure-swap.sh

# Verificar
free -h
```

**Resultado esperado:**
```
               total       usado       libre
Mem:           7.8Gi       2.0Gi       5.8Gi
Swap:          4.0Gi       0.0Gi       4.0Gi
```

---

## 🔧 PASO 2: ACTUALIZAR CÓDIGO

```bash
cd /var/www/zencloud-backend
git pull origin develop
```

---

## 🐳 PASO 3: DESPLEGAR CONTENEDORES

```bash
# Detener todo
docker compose down

# Limpiar contenedores antiguos
docker system prune -f

# Levantar con nuevos recursos
docker compose up -d --build

# Ver logs
docker logs -f ZenCloud
```

---

## ⏱️ PASO 4: ESPERAR INICIALIZACIÓN

**SQL Server tarda ~2 minutos en iniciar con estos recursos.**

```bash
# Esperar 2 minutos
sleep 120

# Verificar SQL Server
docker logs sqlserver-ZenDb --tail 30

# Debe mostrar: "SQL Server is now ready for client connections"
```

---

## ✅ PASO 5: VERIFICAR SERVICIOS

```bash
# Ver todos los contenedores
docker ps

# Verificar recursos
docker stats --no-stream

# Ver logs del backend
docker logs ZenCloud --tail 50
```

---

## 🔍 MONITOREO

### Ver uso de recursos en tiempo real:
```bash
docker stats
```

### Ver uso de memoria del sistema:
```bash
free -h
htop
```

### Ver uso de SWAP:
```bash
swapon --show
vmstat 1 10
```

---

## 🐛 TROUBLESHOOTING

### Si SQL Server no inicia:
```bash
# Ver logs detallados
docker logs sqlserver-ZenDb --tail 100

# Reiniciar solo SQL Server
docker restart sqlserver-ZenDb
sleep 60
docker logs sqlserver-ZenDb --tail 30
```

### Si Cassandra falla:
```bash
# Ver logs
docker logs cassandra-ZenDb --tail 50

# Reiniciar
docker restart cassandra-ZenDb
```

### Si hay problemas de memoria:
```bash
# Ver memoria disponible
free -h

# Ver procesos que más consumen
docker stats --no-stream | sort -k 4 -h

# Reiniciar servicios problemáticos
docker restart <container-name>
```

---

## 📈 OPTIMIZACIONES APLICADAS

✅ **Backend:** 2GB → Soporta más conexiones simultáneas
✅ **SQL Server:** 2GB + 1792MB interno → Inicia correctamente
✅ **Cassandra:** 1.5GB + 1GB heap → NoSQL estable
✅ **PostgreSQL:** 1GB c/u → Mejor caché y queries
✅ **MySQL:** 1GB → InnoDB optimizado
✅ **SWAP:** 4GB → Maneja picos sin OOM kills
✅ **Swappiness:** 60 → Balance entre RAM y SWAP
✅ **Cache pressure:** 50 → Mejor rendimiento I/O

---

## 🎯 SIGUIENTES PASOS

1. ✅ Configurar SWAP
2. ✅ Desplegar con nuevos recursos
3. ✅ Verificar todos los servicios
4. 🧪 Probar creación de bases de datos
5. 📊 Monitorear por 24h
6. 🔧 Ajustar si es necesario

---

## ⚠️ NOTAS IMPORTANTES

- **SQL Server** necesita mínimo 2GB para Express Edition
- **Cassandra** requiere tiempo de warm-up (~1-2 min)
- **SWAP** es CRÍTICO para estabilidad
- Monitorear logs primeras 24h
- Si un servicio falla, reiniciarlo individualmente

**¡Todo optimizado para 8GB RAM!** 🚀
