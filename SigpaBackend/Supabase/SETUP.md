# Configurar Supabase para SIGPA

## 1. Ejecutar el esquema

1. Abre el proyecto de Supabase y entra en **SQL Editor**.
2. Crea una consulta nueva.
3. Copia todo el contenido de [`schema.sql`](./schema.sql), pégalo y ejecuta la consulta. También actualiza las tablas creadas anteriormente con las columnas nuevas que SIGPA necesita.
4. Ejecuta [`extension_academica.sql`](./extension_academica.sql) después del esquema base. Crea cursos, matrículas y las restricciones de equipo/entregas/tareas.
5. Ejecuta [`usuarios_centralizados.sql`](./usuarios_centralizados.sql) al final. Agrega perfiles administrativos, deshabilitación, cursos docentes y los índices para correo/identificación.
6. En **Table Editor**, confirma que existan `usuarios`, `cursos`, `matriculas`, `docentes_cursos`, `proyectos`, `proyecto_integrantes`, `entregables` y `tareas_kanban`.

El script activa Row Level Security. Los usuarios solo leen su perfil; estudiantes crean y consultan sus propios proyectos; profesores, coordinadores y administradores pueden consultar y revisar proyectos. Las notificaciones persistentes están deshabilitadas en la aplicación y no son necesarias para gestionar invitaciones, que se consultan desde los integrantes del proyecto. La contraseña y la clave `service_role` no forman parte del SQL.

## 2. Deshabilitar el autorregistro y crear el primer coordinador

En **Authentication > Sign Up / Providers**, deshabilita el registro público
de correo/contraseña (**Allow new users to sign up**). La ruta heredada
`POST /api/auth/registrar` también está deshabilitada; las cuentas nuevas se
crean en la aplicación por un coordinador autenticado.

El primer coordinador se aprovisiona una sola vez por un administrador de TI:

1. Crea y confirma la cuenta del coordinador en **Authentication > Users > Add user**.
2. En SQL Editor, asigna su rol al perfil creado automáticamente por el trigger:

```sql
update public.usuarios
set rol = 'Coordinador', activo = true, debe_cambiar_contrasena = false
where lower(correo_institucional) = lower('coordinador@universidad.edu');
```

3. Inicia sesión con esa cuenta y crea las cuentas de estudiantes/profesores
   desde **Gestión centralizada de usuarios**.

No se almacena un `password_hash` en `public.usuarios`: Supabase Auth es el
proveedor de credenciales y guarda su hash con salt. SIGPA crea contraseñas
temporales aleatorias mediante Supabase Admin y marca la cuenta para exigir
cambio al primer inicio. Hasta cambiarla, el backend solo permite completar
ese flujo; la nueva contraseña se actualiza en Supabase Auth desde el servidor.
La contraseña nunca se devuelve por la API.

Los perfiles históricos se pueden reconciliar con [`bootstrap_profiles.sql`](./bootstrap_profiles.sql)
solo durante una migración/recuperación controlada; no uses ese script para
crear cuentas de estudiantes o docentes en operación normal.

Si la tabla `proyectos` ya existía antes, ejecuta también [`repair_project_columns.sql`](./repair_project_columns.sql) para agregar las columnas que consume el frontend.
Si Auth valida las credenciales pero la app no puede leer el perfil, ejecuta [`repair_profile_rls.sql`](./repair_profile_rls.sql). Solo concede a cada usuario lectura de su propio perfil.
Si crear un proyecto devuelve un error de Row Level Security, ejecuta [`repair_project_rls.sql`](./repair_project_rls.sql). Permite insertar solo proyectos cuyo `creador_id` sea el usuario autenticado y mantiene la consulta/revisión de staff restringida por rol.
Si el docente no puede cambiar el estado porque una restricción antigua lo rechaza, ejecuta [`repair_project_status.sql`](./repair_project_status.sql) para alinear los estados permitidos con la aplicación.

Comprueba el resultado en SQL Editor:

```sql
select correo_institucional, rol
from public.usuarios
where lower(correo_institucional) in ('estudiante@sigpa.edu', 'profesor@sigpa.edu');
```

El resultado debe contener ambos correos, con roles `Estudiante` y `Profesor`.

## 3. Registrar asignaturas y matrículas

Antes de crear proyectos, el personal administrativo debe registrar cada
asignatura en `public.cursos` con su código, nombre y `docente_id`, y crear
registros en `public.matriculas` para los estudiantes inscritos con estado
`Activa`. `docente_id` debe corresponder a un perfil con rol `Profesor`.
Programa, semestre y periodo académico son opcionales en los registros de
cursos históricos y se pueden completar desde SQL. La API solo ofrece al
estudiante sus matrículas activas; no se deben conceder permisos de lectura
directa de esas tablas al navegador.

## 4. Configurar la aplicación local

En `sigpa-frontend/.env`, configura la URL y la clave **publishable** del proyecto:

```dotenv
VITE_SUPABASE_URL=https://<project-ref>.supabase.co
VITE_SUPABASE_ANON_KEY=<publishable-key>
```

Reinicia Vite después de cambiar `.env`. Ese archivo está excluido de Git. Nunca pongas una clave `service_role` o `sb_secret` en el frontend.

Si se usa el backend .NET, configura sus secretos fuera del repositorio desde la carpeta `SigpaBackend`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string-copied-from-Supabase-Connect>"
dotnet user-secrets set "Supabase:Url" "https://<project-ref>.supabase.co"
dotnet user-secrets set "Supabase:ServiceKey" "<service-role-key>"
dotnet user-secrets set "Smtp:Host" "<smtp-host>"
dotnet user-secrets set "Smtp:Port" "587"
dotnet user-secrets set "Smtp:From" "sigpa@universidad.edu"
dotnet user-secrets set "Smtp:Username" "<smtp-user>"
dotnet user-secrets set "Smtp:Password" "<smtp-password>"
dotnet run
```

Obtén la cadena de conexión PostgreSQL desde **Project Settings > Database > Connect**. La aplicación necesita una cadena de conexión Npgsql válida para acceder a la base de datos; `Supabase:Url` y la clave `service_role` no la sustituyen. En redes que no tienen conectividad IPv6, selecciona **Session Pooler** en lugar de **Direct connection**: el host directo `db.<project-ref>.supabase.co` puede publicar únicamente una dirección IPv6 y no será accesible desde esa red. Copia el host, puerto, usuario y base de datos exactos que muestra Supabase para el pooler de la región del proyecto; no inventes ni reutilices el host directo. También se puede definir `DATABASE_URL` como variable de entorno, siempre con una cadena de conexión PostgreSQL válida. Si `ConnectionStrings:DefaultConnection` está vacío, la aplicación usa `DATABASE_URL`.

Si no configuraste ninguna de las dos opciones, `dotnet run` se detendrá con un mensaje que indica cómo configurarla. Reinicia el backend después de guardar los secretos. No guardes credenciales en `appsettings.json` ni las subas al repositorio.

El SMTP solo se utiliza al seleccionar el envío de bienvenida. Si el proveedor
de correo no está configurado o falla, la cuenta queda creada con cambio de
contraseña obligatorio y la API/pantalla informa que el mensaje quedó pendiente;
el usuario puede iniciar el restablecimiento desde la pantalla de acceso.
Nunca expongas `Supabase:ServiceKey` ni credenciales SMTP en Vite.

## 5. Probar

1. Inicia el frontend y entra con cada cuenta de Supabase.
2. Como estudiante con matrícula activa, registra un proyecto e invita compañeros disponibles; confirma las filas en `public.proyectos` y `public.proyecto_integrantes`.
3. Como profesor, revisa el proyecto y cambia su estado.
4. Configura un hito pendiente y verifica que el estudiante pueda enviar un PDF/ZIP o enlace; prueba también el rechazo fuera de plazo.
5. Crea, asigna, mueve, edita y elimina tareas desde el tablero Kanban.
6. Cierra sesión y vuelve a entrar para comprobar que los cambios persisten.

Si el login autentica al usuario pero dice que no encuentra su perfil, ejecuta `bootstrap_profiles.sql`. Si aparece un error de columna o esquema al cargar proyectos, vuelve a ejecutar `schema.sql` completo y espera a que se recargue el esquema PostgREST. Si el login muestra un error de credenciales, confirma el usuario y la contraseña en Supabase Authentication.
