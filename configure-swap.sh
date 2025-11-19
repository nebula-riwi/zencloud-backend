#!/bin/bash

# Script para configurar SWAP en servidor con 8GB RAM
# Ejecutar como root: sudo bash configure-swap.sh

echo "🔧 Configurando SWAP para servidor de 8GB RAM..."

# 1. Verificar SWAP actual
echo "📊 SWAP actual:"
free -h
swapon --show

# 2. Crear archivo SWAP de 4GB (50% de RAM)
SWAP_SIZE="4G"
SWAP_FILE="/swapfile"

if [ -f "$SWAP_FILE" ]; then
    echo "⚠️  Ya existe un archivo swap. Eliminando..."
    swapoff $SWAP_FILE
    rm $SWAP_FILE
fi

echo "📁 Creando archivo swap de $SWAP_SIZE..."
fallocate -l $SWAP_SIZE $SWAP_FILE || dd if=/dev/zero of=$SWAP_FILE bs=1M count=4096

# 3. Configurar permisos
echo "🔒 Configurando permisos..."
chmod 600 $SWAP_FILE

# 4. Formatear como SWAP
echo "💾 Formateando como SWAP..."
mkswap $SWAP_FILE

# 5. Activar SWAP
echo "✅ Activando SWAP..."
swapon $SWAP_FILE

# 6. Hacer permanente (agregar a /etc/fstab)
if ! grep -q "$SWAP_FILE" /etc/fstab; then
    echo "📝 Agregando a /etc/fstab para que sea permanente..."
    echo "$SWAP_FILE none swap sw 0 0" >> /etc/fstab
fi

# 7. Optimizar swappiness (60 es bueno para servidores)
echo "⚙️  Configurando swappiness=60..."
sysctl vm.swappiness=60
if ! grep -q "vm.swappiness" /etc/sysctl.conf; then
    echo "vm.swappiness=60" >> /etc/sysctl.conf
fi

# 8. Configurar vfs_cache_pressure (recomendado: 50)
echo "⚙️  Configurando vfs_cache_pressure=50..."
sysctl vm.vfs_cache_pressure=50
if ! grep -q "vm.vfs_cache_pressure" /etc/sysctl.conf; then
    echo "vm.vfs_cache_pressure=50" >> /etc/sysctl.conf
fi

# 9. Verificar configuración final
echo ""
echo "✅ ¡SWAP configurado correctamente!"
echo ""
echo "📊 Configuración final:"
free -h
swapon --show
echo ""
echo "Swappiness: $(cat /proc/sys/vm/swappiness)"
echo "VFS Cache Pressure: $(cat /proc/sys/vm/vfs_cache_pressure)"
