# Arquitectura técnica de SIGPA

## 1. Alcance y estado actual

Este documento traduce RF-01–RF-08, CU-01–CU-08, RN-01–RN-03 y RNF-05 a un
diseño implementable. El repositorio parte de React 18/Vite, una API ASP.NET
Core 10, PostgreSQL mediante Npgsql y autenticación Supabase. El esquema actual
ya contiene usuarios, proyectos, integrantes, entregables, criterios de rúbrica
y tareas Kanban. `SigpaBackend/Supabase/extension_academica.sql` añade, sin
reemplazar el esquema existente, objetivos específicos, invitaciones y reglas
de equipo, riesgos, entregas tardías, rúbricas estrictas, calificaciones,
auditoría append-only y un bucket privado de entregables. El frontend existente
continúa usando Supabase directamente para el dashboard y la API se conserva
como superficie complementaria.

Este modelo sigue siendo gradual: aún no existen matrículas y asignaturas
normalizadas enlazadas a un periodo, sincronización LMS efectiva, exportaciones
asíncronas ni un worker de notificaciones. RN-02 usa por ahora el valor heredado
`proyectos.curso`; la migración protege las nuevas operaciones sin reescribir
los proyectos históricos. La extensión SQL no ha sido aplicada a una instancia
remota y debe probarse en staging con copia de seguridad antes de producción.

## 2. Arquitectura propuesta

### Primera etapa: monolito modular

Mantener una API ASP.NET Core desplegable como una unidad, separada internamente
por módulos de dominio. PostgreSQL es la fuente de verdad transaccional. Esto
evita la complejidad operativa de microservicios antes de que el volumen o los
equipos de desarrollo lo justifiquen.

```text
React/Vite
   │ HTTPS + JWT (Supabase Auth / OIDC)
   ▼
ASP.NET Core API (/api/v1)
   ├── Identidad y autorización (RBAC + políticas de recurso)
   ├── Gestión académica y matrículas
   ├── Proyectos y equipos
   ├── Hitos, entregas y almacenamiento
   ├── Kanban y riesgos
   ├── Rúbricas y calificaciones
   ├── Indicadores y reportes
   └── Integraciones, notificaciones y auditoría
   │                       │
   ▼                       ├── Worker + cola de trabajos
PostgreSQL                  │    ├── PDF/XLSX
   │                        │    ├── Correo
   ├── Storage privado      │    └── Sincronización LMS
   └── Supabase Realtime* ◄─┘
```

`*` Realtime se usa para refrescar el Kanban; toda mutación se valida y persiste
primero en la API. Para operación con varias instancias, usar SignalR con back
plane o el mecanismo Realtime existente, además de control de concurrencia
optimista.

### Módulos y responsabilidades

| Módulo | Responsabilidades |
|---|---|
| Identidad y acceso | Validar JWT/OIDC, administrar roles y permisos, autorizar por alcance y recurso. |
| Catálogos académicos | Programas, periodos, asignaturas, docentes titulares y matrículas sincronizadas. |
| Proyectos y equipos | Propuestas, dictámenes, invitaciones, pertenencia y reglas de cupo. |
| Hitos y entregas | Calendario, política de prórroga, carga segura, historial y sello temporal. |
| Colaboración | Tableros Kanban, asignación obligatoria y bitácora de riesgos. |
| Evaluación | Versiones de rúbrica, calificación por criterio, nota ponderada y publicación. |
| Analítica y reportes | KPIs filtrados por alcance, detección de retrasos y exportaciones. |
| Integraciones | Adaptadores Moodle/Canvas, LTI 1.3, OAuth2, webhooks y reintentos. |
| Plataforma y auditoría | Trabajos asíncronos, correo, almacenamiento y eventos de auditoría retenidos cinco años. |

Se recomienda separar cada módulo en `Domain`, `Application`, `Infrastructure`
y `Api` solo cuando esa separación ayude a mantener límites reales. Para el
estado actual basta con organizar proyectos/carpetas por módulo y registrar
servicios por interfaz en ASP.NET Core; no crear microservicios en esta etapa.

## 3. Modelo relacional objetivo

Todas las claves primarias son UUID. Fechas almacenadas en `timestamptz` UTC;
fechas límite se interpretan en la zona horaria configurada para el periodo o
la asignatura y se convierten a UTC al persistir. Aplicar `ON DELETE RESTRICT`
en datos académicos publicados y usar borrado lógico/estados cuando se requiera
conservar historial.

### 3.1 Identidad y estructura académica

| Tabla | Columnas principales y restricciones |
|---|---|
| `usuarios` | `id` (referencia al usuario de Supabase Auth), `nombre_completo`, `correo_institucional UNIQUE`, `activo`, `created_at`. No guardar contraseñas localmente. |
| `roles` | `id`, `codigo UNIQUE` (`ESTUDIANTE`, `DOCENTE`, `COORDINADOR`, `ADMIN_TI`), `nombre`. |
| `permisos` | `id`, `codigo UNIQUE`, `descripcion`. |
| `rol_permisos` | `rol_id`, `permiso_id`, PK compuesta. |
| `asignaciones_rol` | `usuario_id`, `rol_id`, `alcance_tipo`, `alcance_id NULL`, `vigente_desde`, `vigente_hasta NULL`; índices por usuario y alcance. Permite que el docente y coordinador tengan alcance institucional o acotado. |
| `programas` | `id`, `codigo UNIQUE`, `nombre`, `activo`. |
| `periodos` | `id`, `codigo UNIQUE`, `inicio`, `fin`, `zona_horaria`, `estado`; `inicio < fin`. |
| `asignaturas` | `id`, `programa_id`, `codigo`, `nombre`, `periodo_id`, `docente_id`, `activo`; `UNIQUE(periodo_id, codigo)` y FK al programa, periodo y docente. |
| `matriculas` | `asignatura_id`, `estudiante_id`, `estado`, `created_at`; PK/UNIQUE `(asignatura_id, estudiante_id)`. El estudiante debe estar activo para incorporarse a un equipo. |

### 3.2 Propuestas y equipos

| Tabla | Columnas principales y restricciones |
|---|---|
| `proyectos` | `id`, `asignatura_id`, `titulo`, `problema`, `objetivo_general`, `estado`, `creador_id`, `observaciones_docente`, `fecha_correccion_hasta`, `created_at`, `updated_at`, `version`; `UNIQUE(id, asignatura_id)` para soportar FK compuesta del equipo. |
| `objetivos_especificos` | `id`, `proyecto_id`, `descripcion`, `orden`; `UNIQUE(proyecto_id, orden)`. |
| `proyecto_integrantes` | `id`, `proyecto_id`, `asignatura_id`, `estudiante_id`, `estado` (`INVITADO`, `ACEPTADO`, `RECHAZADO`, `RETIRADO`), `invitado_por`, `respondido_at`; FK `(proyecto_id, asignatura_id)` a proyecto, FK `(asignatura_id, estudiante_id)` a matrícula, `UNIQUE(proyecto_id, estudiante_id)`. Índice único parcial `(asignatura_id, estudiante_id) WHERE estado IN ('INVITADO','ACEPTADO')`: bloquea doble asignación/invitación activa en la misma asignatura. |

Cada proyecto corresponde a un equipo de una asignatura. El equipo incluye al
creador como integrante aceptado; su creación y membresía inicial se hacen en
una sola transacción. Un trigger diferible o un servicio de dominio con bloqueo
de fila (`SELECT ... FOR UPDATE` del proyecto) impide más de cinco integrantes
aceptados y evita carreras al aceptar invitaciones. El rango permitido se
valida al confirmar el equipo, con mínimo 1 y máximo 5 estudiantes matriculados.

### 3.3 Hitos, archivos y entregas

| Tabla | Columnas principales y restricciones |
|---|---|
| `hitos` | `id`, `asignatura_id`, `nombre`, `descripcion`, `fecha_limite`, `peso_nota`, `permite_tardias`, `penalizacion_pct`, `estado`; `peso_nota >= 0`, penalización entre 0 y 100. |
| `entregas` | `id`, `hito_id`, `proyecto_id`, `enviada_por`, `enviada_at`, `estado` (`A_TIEMPO`, `TARDIA`), `comentario`, `created_at`; índice `(hito_id, proyecto_id, enviada_at DESC)`. Cada envío es una versión inmutable. |
| `archivos_entrega` | `id`, `entrega_id`, `storage_key`, `nombre_original`, `content_type`, `tamano_bytes`, `sha256`; `CHECK(tamano_bytes BETWEEN 1 AND 26214400)`. |
| `enlaces_entrega` | `id`, `entrega_id`, `url`; solo HTTPS y destinos admitidos validados por aplicación. |

Se conserva una fila por envío, no se sobrescribe la anterior. La hora de
recepción se asigna en servidor/DB, nunca se acepta del cliente. El estado
temprano/tardío se calcula contra la fecha límite y la política efectiva de
prórroga. Una restricción de aplicación limita archivos a PDF o ZIP, valida
contenido real (no solo extensión/MIME), y aplica 25 MiB por archivo. El
contenido binario reside en bucket privado; en PostgreSQL solo se guarda la
clave y metadatos. El acceso a descarga usa URLs firmadas de corta duración.

### 3.4 Rúbricas y calificaciones

| Tabla | Columnas principales y restricciones |
|---|---|
| `rubricas` | `id`, `hito_id`, `version`, `estado` (`BORRADOR`, `PUBLICADA`, `ARCHIVADA`), `created_by`, `published_at`; `UNIQUE(hito_id, version)`. Publicadas son inmutables; cambios generan versión nueva. |
| `criterios_rubrica` | `id`, `rubrica_id`, `nombre`, `descripcion`, `peso_pct`; `CHECK(peso_pct > 0 AND peso_pct <= 100)`. |
| `niveles_rubrica` | `id`, `criterio_id`, `nivel`, `etiqueta`, `descripcion`, `puntaje`; `UNIQUE(criterio_id, nivel)`, puntaje dentro de escala definida. |
| `evaluaciones` | `id`, `entrega_id`, `rubrica_id`, `evaluador_id`, `estado`, `puntaje_final`, `observacion_general`, `evaluada_at`, `publicada_at`; `UNIQUE(entrega_id, rubrica_id)` si solo se conserva la calificación final por versión de rúbrica. |
| `evaluacion_criterios` | `evaluacion_id`, `criterio_id`, `nivel_id`, `puntaje_obtenido`, `observacion`; `UNIQUE(evaluacion_id, criterio_id)`. |

RN-03 se aplica al **publicar** una rúbrica: debe tener al menos un criterio y
`SUM(criterios_rubrica.peso_pct) = 100.00`. Un `CHECK` normal no puede validar
la suma entre filas. Implementar trigger de restricción diferible (`DEFERRABLE
INITIALLY DEFERRED`) o función de publicación transaccional que bloquee la
rúbrica, valide la suma en base de datos y cambie el estado a `PUBLICADA` en
la misma transacción. La API también valida para entregar errores claros, pero
la validación de base de datos es la garantía final.

Puntaje final en escala 0–100:

```text
puntaje_final = SUM(puntaje_normalizado_criterio * peso_pct / 100)
```

El cálculo, redondeo y escala deben ser únicos en un servicio de dominio. La
nota no es visible al estudiante hasta que un docente la publique. La
publicación crea un evento de outbox para correo y sincronización LMS, sin
mantener abierta la transacción mientras se llaman servicios externos.

### 3.5 Kanban, riesgos, reportes e integración

| Tabla | Columnas principales y restricciones |
|---|---|
| `tareas_kanban` | `id`, `proyecto_id`, `titulo`, `descripcion`, `estado` (`POR_HACER`, `EN_PROCESO`, `HECHO`), `responsable_id NOT NULL`, `fecha_compromiso`, `orden`, `version`, `updated_at`. FK compuesta `(proyecto_id, responsable_id)` a integrantes aceptados. |
| `riesgos` | `id`, `proyecto_id`, `reportado_por`, `titulo`, `descripcion`, `severidad`, `estado`, `created_at`, `resuelto_at`; solo integrantes pueden crear y docentes/coordinadores con alcance pueden consultar/gestionar. |
| `eventos_auditoria` | `id`, `actor_id NULL`, `accion`, `recurso_tipo`, `recurso_id`, `ocurrido_at`, `ip_hash NULL`, `correlation_id`, `datos JSONB` minimizados. Solo INSERT para el rol de aplicación; UPDATE/DELETE revocados. Retención mínima cinco años y exportación controlada. |
| `trabajos_asincronos` | `id`, `tipo`, `estado`, `parametros JSONB`, `solicitado_por`, `created_at`, `started_at`, `completed_at`, `error_code`, `storage_key`; índices por estado/fecha. |
| `outbox_eventos` | `id`, `tipo`, `aggregate_id`, `payload JSONB`, `created_at`, `procesado_at`, `intentos`; publicación eventual fiable de correos/LMS. |
| `integraciones_lms` | `id`, `asignatura_id`, `proveedor`, `external_course_id`, `estado`, `secret_ref`; secretos en vault, nunca en filas/logs. |

Los indicadores se calculan con consultas agregadas/vistas materializadas
filtradas por programa, asignatura, docente y periodo. Un proyecto se considera
en riesgo usando un umbral configurable por periodo/asignatura y un cálculo
reproducible; no codificar un umbral institucional fijo sin decisión académica.

## 4. Reglas de negocio e integridad

| Regla | Aplicación |
|---|---|
| RN-01 Entrega tardía/prórroga | Resolver límite efectivo y penalización con configuración vigente al envío. Se marca `TARDIA` si el timestamp de servidor supera el límite efectivo. No descartar automáticamente una entrega tardía si el hito la permite. Guardar la regla aplicada para auditoría. |
| RN-02 Integrantes | Matrícula activa obligatoria; máximo cinco aceptados; invitación/aceptación y cupo se actualizan bajo transacción y bloqueo; índice único parcial evita estar en dos equipos activos de la misma asignatura. |
| RN-03 Rúbrica | La suma de criterios publicada es exactamente 100.00%; validación transaccional en DB al publicar. La edición de publicada requiere nueva versión. |
| RF-01 Propuesta | Campos requeridos y objetivos específicos no vacíos. Rechazo requiere observaciones y fija `fecha_correccion_hasta = dictamen_at + 3 días`. |
| RF-02 Hitos | Fechas coherentes con periodo. Si las ponderaciones de hitos componen nota final del curso, también validarlas al publicar en 100%, explícitamente aparte de RN-03. |
| RF-03 Archivos | Máximo 25 MiB por objeto, PDF/ZIP únicamente; subir a almacenamiento privado, analizar contenido y persistir metadatos tras completar la carga. |
| RF-04 Calificar/publicar | Solo docente con asignatura en alcance puede evaluar/publicar. Publicación idempotente genera outbox una vez. |
| RF-06 LMS | LTI 1.3/OAuth2 y adaptador por proveedor. Tokenización idempotente, reintentos con backoff y estado de sincronización trazable; no acoplar dominio a Moodle o Canvas. |
| RF-08 Reportes | Hasta 500 proyectos puede generarse síncronamente si el límite de tiempo del API lo permite; más de 500 siempre en background. Responder `202 Accepted` con URI de estado y notificar por correo al terminar. |
| RNF-05 Auditoría | Auditoría append-only, acceso separado, respaldos y retención de cinco años. No registrar tokens, contraseñas, secretos ni contenido sensible de entregas. |

## 5. RBAC y autorización por recurso

Los códigos de rol sugeridos son `ESTUDIANTE`, `DOCENTE`, `COORDINADOR` y
`ADMIN_TI`. El usuario puede tener más de un rol; una asignación guarda el
alcance (`GLOBAL`, `PROGRAMA`, `ASIGNATURA`) y vigencia. La autorización combina
**permiso RBAC** con una política de recurso: por ejemplo, ser docente no
permite calificar asignaturas que no tiene asignadas; ser estudiante no
permite modificar otro equipo.

| Operación/permiso | Estudiante | Docente | Coordinador | Admin TI |
|---|---:|---:|---:|---:|
| Leer/editar su perfil | Sí | Sí | Sí | Sí |
| Crear propuesta / invitar compañeros | Sí, matriculado | No | No | No |
| Leer y gestionar equipo/proyecto | Solo su equipo | Asignaturas en alcance | Lectura en alcance | Solo soporte autorizado |
| Dictaminar propuesta / configurar hitos | No | Asignaturas en alcance | No (supervisión) | No |
| Entregar avance / gestionar Kanban / reportar riesgo | Su equipo | Lectura/gestión docente según política | Lectura en alcance | No |
| Crear/publicar rúbrica y calificar | No | Asignaturas en alcance | No | No |
| Ver notas | Solo publicadas de su equipo | Asignaturas en alcance | Agregados, no detalle personal salvo permiso explícito | No |
| Consultar KPIs / exportar | No | Su asignatura | Sí, según alcance institucional | No por defecto |
| Asignar roles / catálogos / auditoría técnica | No | No | No | Sí, cada permiso por separado |

Permisos atómicos sugeridos: `project.create`, `project.review`,
`team.invite`, `team.accept`, `milestone.manage`, `submission.create`,
`kanban.manage`, `risk.create`, `risk.manage`, `rubric.manage`,
`grade.evaluate`, `grade.publish`, `analytics.read`, `report.export`,
`role.assign`, `catalog.manage`, `audit.read`.

En ASP.NET Core implementar policies con `IAuthorizationHandler` que comprueben
rol, alcance académico, membresía/matrícula y estado del recurso. Validar
`sub` del JWT firmado de Supabase y consultar la asignación vigente en servidor;
no confiar en un rol enviado por el cliente ni en claims de rol potencialmente
obsoletos. Los endpoints por defecto requieren autenticación. No exponer una
clave `service_role` al frontend. Si el frontend usa PostgREST directamente,
habilitar RLS equivalente para cada tabla y evitar que una política amplia de
“staff” conceda acceso global a docentes.

## 6. API REST principal

Prefijo recomendado: `/api/v1`. Respuestas JSON en camelCase, errores
`ProblemDetails`, paginación cursor/limit y `traceId`. `If-Match`/`version`
para mutaciones concurrentes de Kanban, propuestas y evaluación. Los endpoints
indicados requieren JWT salvo que se señale expresamente.

La implementación actual expone rutas `/api/...` sin versión en URL. Están
implementadas `POST/GET /api/proyectos`, `GET /api/proyectos/mis-proyectos`,
`GET /api/proyectos/{id}`, `GET /api/proyectos/{id}/objetivos`,
`POST /api/proyectos/{id}/correcciones`, operaciones de
`integrantes`/`invitaciones`, `PATCH /api/proyectos/{id}/evaluar`,
`GET/POST /api/proyectos/{id}/tareas`, `PATCH /api/tareas/{id}/estado`,
`POST /api/entregables`, `POST /api/entregables/{id}/entregar`,
`POST/GET /api/entregables/{id}/archivo`,
`POST /api/entregables/{id}/rubrica`, operaciones de
`/api/entregables/{id}/evaluaciones`, `POST /api/evaluaciones/{id}/publicar`,
`GET /api/proyectos/{id}/calificaciones` y operaciones de
`/api/proyectos/{id}/riesgos`. El versionado `/api/v1` sigue siendo objetivo.

| Método y ruta | Acceso | Uso |
|---|---|---|
| `GET /me` | Autenticado | Perfil, roles y alcances vigentes. |
| `GET /periodos/{periodoId}/asignaturas` | Matriculado/docente/coordinador según alcance | Asignaturas visibles. |
| `POST /asignaturas/{asignaturaId}/proyectos` | Estudiante matriculado | Crear propuesta y membresía inicial atómicamente. |
| `GET /proyectos?asignaturaId=&estado=&cursor=` | Según alcance/membresía | Lista filtrada y paginada. |
| `GET /proyectos/{id}` | Integrante/docente asignado/coordinador autorizado | Detalle y equipo. |
| `PATCH /proyectos/{id}` | Creador/equipo dentro de estado permitido | Editar propuesta antes de dictamen o corrección. |
| `POST /proyectos/{id}/invitaciones` | Miembro autorizado, estudiante matriculado | Invitar por `estudianteId`; no exceder cupo. |
| `POST /invitaciones/{id}/aceptar` / `rechazar` | Destinatario matriculado | Responder y aplicar exclusividad/cupo transaccionalmente. |
| `PUT /proyectos/{id}/dictamen` | Docente asignado | `APROBADO`/`RECHAZADO`, comentarios requeridos en rechazo; fija plazo de tres días. |
| `POST /asignaturas/{id}/hitos` / `PATCH /hitos/{id}` | Docente asignado | Configurar calendario, ponderación y política de tardanza. |
| `POST /hitos/{id}/rubricas` | Docente asignado | Crear borrador y criterios. |
| `POST /rubricas/{id}/publicar` | Docente asignado | Validar suma 100% y publicar en transacción. |
| `POST /proyectos/{id}/hitos/{hitoId}/entregas` | Integrante | Registrar envío; metadata/enlace o token de carga del archivo. |
| `POST /entregas/{id}/archivos` | Integrante | Flujo de carga firmado; 25 MiB máximo. Preferir URL prefirmada y callback de confirmación. |
| `GET /proyectos/{id}/hitos/{hitoId}/entregas` | Equipo/docente asignado | Historial de versiones y estado temporal. |
| `POST /entregas/{id}/evaluacion` | Docente asignado | Puntuar criterios y observaciones; deja nota en borrador. |
| `POST /evaluaciones/{id}/publicar` | Docente asignado | Publicar nota, crear outbox de correo y LMS. |
| `GET /proyectos/{id}/calificaciones` | Integrante solo tras publicar; docente | Nota y detalle por criterio según permiso. |
| `GET /proyectos/{id}/tablero` | Integrante/docente asignado | Leer tarjetas ordenadas por columna. |
| `POST /proyectos/{id}/tareas` | Integrante | Crear tarea; `responsableId` obligatorio y miembro del equipo. |
| `PATCH /tareas/{id}` | Integrante | Mover/editar tarjeta con `version` o `If-Match`. |
| `POST /proyectos/{id}/riesgos` | Integrante | Registrar riesgo o bloqueo. |
| `GET /analitica/kpis?periodoId=&programaId=&asignaturaId=&docenteId=` | Coordinador en alcance | Entregas a tiempo/tarde, nota media, avance y riesgos. |
| `POST /reportes` | Coordinador en alcance | Solicitar PDF/XLSX; `202` y `jobId` si volumen >500. |
| `GET /reportes/{jobId}` | Solicitante/autorizado | Estado y enlace firmado al resultado. |
| `POST /integraciones/lms/{asignaturaId}/sincronizar` | Servicio/docente autorizado | Sincronizar matrícula o reintentar nota. |
| `POST /webhooks/lms/{proveedor}` | LMS autenticado | Eventos firmados/idempotentes del proveedor. |
| `GET /admin/auditoria` | `audit.read` | Consulta paginada y acotada de auditoría. |
| `POST /admin/roles` / `PUT /admin/usuarios/{id}/roles` | `role.assign` | Gestión administrativa auditada. |

La autenticación SSO debe delegarse a Supabase OIDC o al IdP institucional; no
crear un endpoint que almacene o procese contraseñas. Para LTI 1.3 implementar
los endpoints de inicio OIDC y callback/launch siguiendo el protocolo en un
adaptador separado; el JWT de la API no sustituye la validación LTI.

## 7. Estrategia de implementación

### Implementado en esta iteración

- Migración aditiva `SigpaBackend/Supabase/extension_academica.sql`; aplicar
  después de `schema.sql`. No se modificó ni sustituyó el esquema base.
- Políticas de proyecto, equipo y entregables revisadas para preservar el alta
  del dashboard actual y restringir cambios de estado a personal docente.
- Registro público limitado a cuentas Estudiante y validación JWT Supabase
  obligatoria; ya no se aceptan roles privilegiados en el formulario de registro.
- Propuestas con objetivos, dictamen docente, plazo de corrección de tres días,
  invitaciones con aceptación, y bloqueos de cupo/asignación con transacción y
  trigger PostgreSQL.
- Tareas Kanban con responsable requerido, entregas puntuales/extemporáneas,
  carga PDF/ZIP de hasta 25 MiB a Storage privado, notas ponderadas, publicación,
  descarga firmada de corta duración y bitácora de riesgos.
- Eventos de auditoría inmutables para las mutaciones principales.

Para el backend configura `Supabase:Url`, `Supabase:ServiceKey`,
`Supabase:DeliverablesBucket` (por defecto `entregables`), `DATABASE_URL` o
`ConnectionStrings:DefaultConnection`, y `Cors:AllowedOrigins`. La clave de
servicio solo puede estar en el servidor. La extensión crea/asegura el bucket
privado `entregables` con límite de 25 MiB y MIME PDF/ZIP. La API usa una
conexión de base de datos privilegiada del servidor para las operaciones
protegidas; no enviar la clave service-role al navegador.

### Pendiente de integración

1. Normalizar programas, periodos, asignaturas y matrículas y validar
   matriculación activa desde una fuente institucional.
2. Implementar asignación de docentes por asignatura y alcance institucional en
   vez de conceder alcance global por el rol docente actual.
3. Incorporar niveles de logro por criterio y versionado de rúbrica/calificación.
4. Ejecutar un worker/outbox real para correo y sincronización Moodle/Canvas;
   los endpoints de integración LTI/OAuth2 requieren configuración del LMS.
5. Implementar KPIs del coordinador, alertas con umbral académico configurable,
   PDF/XLSX y procesamiento en segundo plano para más de 500 proyectos.
6. Conectar las nuevas pantallas de riesgo, rúbrica, evaluación y entregas al
   API; el dashboard actual mantiene sus lecturas/escrituras Supabase existentes.

Pruebas mínimas antes de producción: restricciones y migraciones sobre una DB
PostgreSQL real; pruebas concurrentes de invitaciones/cupo; rúbrica con sumas
99.99, 100 y 100.01; entrega justo antes/después del límite y con prórroga;
matriz de permisos por rol y alcance; carga de tipo/tamaño no permitido;
reintentos idempotentes de publicación y LMS; y retención/lectura de auditoría.
