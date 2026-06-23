# MMORPG Framework Kubernetes Deployment Guide
# Copyright (c) 2024-2026 MMORPG Framework Contributors
# SPDX-License-Identifier: MIT

# Prerequisites

Before deploying MMORPG Framework to Kubernetes, ensure you have:

1. **Kubernetes Cluster** (v1.24+)
   - Minimum 2 nodes
   - 4 CPU cores, 8GB RAM per node recommended
   - Kubernetes admin access

2. **Helm** (v3.8+)
   ```bash
   # Install Helm
   curl -fsSL https://get.helm.sh/helm-v3.8.0-linux-amd64.tar.gz | tar -xz
   sudo mv linux-amd64/helm /usr/local/bin/helm
   rm -rf linux-amd64
   ```

3. **kubectl** configured with cluster access
   ```bash
   kubectl config current-context
   ```

4. **Docker** (for building images)
   ```bash
   docker build -t mmorpg/framework:latest .
   ```

---

## Installation

### 1. Add Helm Repository (if using remote chart)

```bash
helm repo add mmorpg https://charts.mmorpg.example.com
helm repo update
```

### 2. Install Chart from Local Directory

```bash
# Navigate to project directory
cd mmorpg

# Create namespace
kubectl create namespace mmorpg-framework

# Install chart
helm install mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --create-namespace

# Verify installation
kubectl get pods -n mmorpg-framework
```

### 3. Install with Custom Values

```bash
# Using custom values file
helm install mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --values ./deploy/kubernetes/values-production.yaml

# Or using command-line overrides
helm install mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --set replicaCount=3 \
  --set autoscaling.enabled=true \
  --set autoscaling.minReplicas=2 \
  --set autoscaling.maxReplicas=20
```

---

## Configuration

### Essential Configuration

| Parameter | Description | Default |
|----------|-------------|---------|
| `replicaCount` | Number of replicas | `1` |
| `image.repository` | Docker image repository | `mmorpg/framework-test` |
| `image.tag` | Docker image tag | `latest` |
| `service.type` | Service type | `ClusterIP` |
| `app.config.tcpPort` | TCP server port | `7001` |
| `app.config.httpPort` | HTTP API port | `8080` |
| `app.config.prometheusPort` | Prometheus metrics port | `9091` |
| `autoscaling.enabled` | Enable HPA | `true` |
| `autoscaling.minReplicas` | Minimum replicas | `1` |
| `autoscaling.maxReplicas` | Maximum replicas | `10` |

### Resource Limits

```yaml
# values.yaml
resources:
  limits:
    cpu: "2000m"
    memory: "2Gi"
  requests:
    cpu: "500m"
    memory: "512Mi"
```

### Network Configuration

```yaml
# values.yaml
app:
  config:
    tcpPort: 7001
    httpPort: 8080
    prometheusPort: 9091
    maxConnections: 10000
    heartbeatTimeoutSeconds: 30
```

---

## Upgrading

### Upgrade to New Version

```bash
# Pull latest chart
helm repo update

# Upgrade release
helm upgrade mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework

# Check upgrade status
kubectl rollout status deployment/mmorpg-framework -n mmorpg-framework
```

### Rolling Update

```bash
# Trigger rolling update by updating image
helm upgrade mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --set image.tag=v1.2.0

# Check rollout status
kubectl rollout status deployment/mmorpg-framework -n mmorpg-framework
```

---

## Monitoring

### Enable Prometheus Metrics

```yaml
# values.yaml
monitoring:
  enabled: true
  prometheus:
    enabled: true
    port: 9091
    path: /metrics
```

### Access Metrics

```bash
# Port-forward to access metrics locally
kubectl port-forward svc/mmorpg-framework 9091:9091 -n mmorpg-framework

# Open browser
open http://localhost:9091/metrics
```

### Grafana Dashboard

```bash
# Add Grafana dashboard
kubectl apply -f ./deploy/grafana/mmorpg-dashboard.json
```

---

## Troubleshooting

### Check Pod Status

```bash
# List all pods
kubectl get pods -n mmorpg-framework

# Get pod details
kubectl describe pod <pod-name> -n mmorpg-framework

# View logs
kubectl logs <pod-name> -n mmorpg-framework -f
```

### Common Issues

#### Pod not starting

```bash
# Check events
kubectl get events -n mmorpg-framework --sort-by='.lastTimestamp'

# Check resource quotas
kubectl describe resourcequota -n mmorpg-framework
```

#### Out of memory

```bash
# Increase memory limits
helm upgrade mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --set resources.limits.memory=4Gi
```

#### High CPU usage

```bash
# Enable autoscaling
helm upgrade mmorpg-framework ./deploy/kubernetes \
  --namespace mmorpg-framework \
  --set autoscaling.enabled=true \
  --set autoscaling.minReplicas=2 \
  --set autoscaling.maxReplicas=20
```

---

## Uninstallation

```bash
# Uninstall release
helm uninstall mmorpg-framework -n mmorpg-framework

# Delete namespace (optional)
kubectl delete namespace mmorpg-framework

# Verify cleanup
kubectl get all -n mmorpg-framework
```

---

## Production Checklist

- [ ] Set resource limits appropriately
- [ ] Enable autoscaling for production workloads
- [ ] Configure monitoring and alerting
- [ ] Set up persistent storage for logs
- [ ] Configure health checks
- [ ] Set up network policies
- [ ] Enable TLS/SSL for ingress
- [ ] Configure backup strategy
- [ ] Test disaster recovery procedures

---

## Support

For issues and questions:
- GitHub Issues: https://github.com/your-org/mmorpg/issues
- Documentation: https://docs.mmorpg.example.com
