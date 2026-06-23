# Common labels
{{- define "mmorpg-framework.labels" -}}
helm.sh/chart: {{ include "mmorpg-framework.chart" . }}
{{ include "mmorpg-framework.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

# Selector labels
{{- define "mmorpg-framework.selectorLabels" -}}
app.kubernetes.io/name: {{ include "mmorpg-framework.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

# Chart name
{{- define "mmorpg-framework.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trimSuffix "-" }}
{{- end -}}

# Full name
{{- define "mmorpg-framework.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end -}}

# Service account name
{{- define "mmorpg-framework.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- include "mmorpg-framework.fullname" . -}}
{{- else }}
{{- .Values.serviceAccount.name | default "default" -}}
{{- end }}
{{- end -}}
