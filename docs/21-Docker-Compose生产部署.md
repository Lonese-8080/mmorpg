# MMORPG Framework Production Docker Compose Guide
# Copyright (c) 2024-2026 MMORPG Framework Contributors
# SPDX-License-Identifier: MIT

# Production Deployment with Docker Compose

This guide covers deploying MMORPG Framework in production using Docker Compose.

---

## 1. Production Configuration

### 1.1 docker-compose.production.yml

Create a production-optimized compose file:

```yaml
version: '3.9'

services:
  mmorpg-framework:
    build:
      context: .
      dockerfile: Dockerfile
      target: production
    image: mmorpg/framework:latest
    container_name: mmorpg-framework
    restart: unless-stopped
    ports:
      - "7001:7001"   # TCP game port
      - "8080:8080"   # HTTP API
      - "9091:9091"   # Prometheus metrics
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DOTNET_EnableDiagnostics=0
      - DOTNET_gcServer=1
      - TCP_PORT=7001
      - HTTP_PORT=8080
      - PROMETHEUS_PORT=9091
      - TARGET_FPS=30
      - MAX_CONNECTIONS=10000
      - HEARTBEAT_TIMEOUT=30
      - ENABLE_METRICS=1
      - ENABLE_TRACING=1
      - API_KEY=${API_KEY}
    volumes:
      - ./logs:/app/logs:rw
      - ./config:/app/config:ro
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    deploy:
      resources:
        limits:
          cpus: '2.0'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"
    networks:
      - mmorpg-net

  prometheus:
    image: prom/prometheus:latest
    container_name: mmorpg-prometheus
    restart: unless-stopped
    ports:
      - "9090:9090"
    volumes:
      - ./deploy/prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus-data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--storage.tsdb.retention.time=15d'
      - '--web.enable-lifecycle'
    networks:
      - mmorpg-net

  grafana:
    image: grafana/grafana:latest
    container_name: mmorpg-grafana
    restart: unless-stopped
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_USER=admin
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD}
      - GF_USERS_ALLOW_SIGN_UP=false
    volumes:
      - ./deploy/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./deploy/grafana/dashboards:/var/lib/grafana/dashboards:ro
      - grafana-data:/var/lib/grafana
    networks:
      - mmorpg-net

  # Optional: Redis for distributed caching
  redis:
    image: redis:7-alpine
    container_name: mmorpg-redis
    restart: unless-stopped
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    command: redis-server --appendonly yes --maxmemory 512mb --maxmemory-policy allkeys-lru
    networks:
      - mmorpg-net

volumes:
  prometheus-data:
  grafana-data:
  redis-data:

networks:
  mmorpg-net:
    driver: bridge
    ipam:
      config:
        - subnet: 172.28.0.0/16
```

---

## 2. Environment Variables

### 2.1 .env.production

```bash
# Framework Configuration
API_KEY=your-secure-api-key-here
TARGET_FPS=30
MAX_CONNECTIONS=10000
HEARTBEAT_TIMEOUT=30

# Monitoring
GRAFANA_PASSWORD=your-secure-grafana-password

# Redis (if using)
REDIS_HOST=redis
REDIS_PORT=6379
```

### 2.2 Security Best Practices

```bash
# Generate secure API key
openssl rand -base64 32

# Generate secure Grafana password
openssl rand -base64 24

# Never commit .env files to version control
echo ".env*" >> .gitignore
```

---

## 3. Deployment Steps

### 3.1 Prepare Environment

```bash
# Create necessary directories
mkdir -p logs config
chmod 755 logs config

# Set permissions
chmod 600 .env.production

# Pull latest images
docker compose -f docker-compose.production.yml pull
```

### 3.2 Deploy

```bash
# Build application
docker compose -f docker-compose.production.yml build --no-cache

# Start services
docker compose -f docker-compose.production.yml up -d

# Verify deployment
docker compose -f docker-compose.production.yml ps
```

### 3.3 Verify Health

```bash
# Check framework health
curl http://localhost:8080/health | jq

# Check metrics
curl http://localhost:9091/metrics | head -20

# View logs
docker compose -f docker-compose.production.yml logs -f mmorpg-framework
```

---

## 4. Update & Rollback

### 4.1 Update

```bash
# Pull latest code
git pull origin main

# Rebuild and restart
docker compose -f docker-compose.production.yml build --no-cache
docker compose -f docker-compose.production.yml up -d

# Verify update
curl http://localhost:8080/test/results | jq
```

### 4.2 Rollback

```bash
# List previous images
docker images | grep mmorpg/framework

# Rollback to previous version
docker tag mmorpg/framework:<previous-tag> mmorpg/framework:latest
docker compose -f docker-compose.production.yml up -d
```

---

## 5. Backup & Recovery

### 5.1 Backup

```bash
# Backup logs
tar -czf backup-logs-$(date +%Y%m%d).tar.gz logs/

# Backup configuration
tar -czf backup-config-$(date +%Y%m%d).tar.gz config/

# Backup Prometheus data (optional, large)
docker run --rm -v prometheus-data:/data -v $(pwd):/backup alpine tar -czf /backup/prometheus-backup.tar.gz /data
```

### 5.2 Recovery

```bash
# Stop services
docker compose -f docker-compose.production.yml down

# Restore logs
tar -xzf backup-logs-YYYYMMDD.tar.gz

# Restore config
tar -xzf backup-config-YYYYMMDD.tar.gz

# Restart services
docker compose -f docker-compose.production.yml up -d
```

---

## 6. Monitoring

### 6.1 Access Grafana

```bash
# Open Grafana dashboard
open http://localhost:3000

# Login with admin credentials
# Default: admin / (GRAFANA_PASSWORD from .env)
```

### 6.2 Custom Dashboards

Import dashboards from `deploy/grafana/dashboards/`.

### 6.3 Alerting

Configure alerts in Grafana or Prometheus:
- High error rate
- High memory usage
- Service down
- Performance degradation

---

## 7. Security Checklist

- [ ] Use secure API keys
- [ ] Enable TLS for production
- [ ] Restrict port access with firewall
- [ ] Regular security scanning with Trivy
- [ ] Keep Docker images updated
- [ ] Review container permissions
- [ ] Enable audit logging
- [ ] Use secrets management

---

## 8. Troubleshooting

### Service won't start

```bash
# Check logs
docker compose -f docker-compose.production.yml logs mmorpg-framework

# Check resource usage
docker stats

# Verify ports not in use
netstat -tulpn | grep -E '7001|8080|9091'
```

### High memory usage

```bash
# Check container memory
docker stats --no-stream

# Increase swap
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
```

### Performance issues

```bash
# Enable debug logging
docker compose -f docker-compose.production.yml exec mmorpg-framework \
  env DEBUG=1 dotnet mmorpg.dll

# Profile CPU
docker compose -f docker-compose.production.yml exec mmorpg-framework \
  dotnet-counters monitor
```

---

## 9. Quick Reference

```bash
# Start
docker compose -f docker-compose.production.yml up -d

# Stop
docker compose -f docker-compose.production.yml down

# Restart
docker compose -f docker-compose.production.yml restart

# View logs
docker compose -f docker-compose.production.yml logs -f

# Scale
docker compose -f docker-compose.production.yml up -d --scale mmorpg-framework=3

# Update
docker compose -f docker-compose.production.yml pull && \
docker compose -f docker-compose.production.yml up -d
```
