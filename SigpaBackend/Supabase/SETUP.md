# Configurar Supabase para SIGPA

## 1. Ejecutar el esquema

1. Abre el proyecto de Supabase y entra en **SQL Editor**.
2. Crea una consulta nueva.
3. Copia todo el contenido de [`schema.sql`](./schema.sql), pégalo y ejecuta la consulta. También actualiza las tablas creadas anteriormente con las columnas nuevas que SIGPA necesita.
4. Ejecuta [`extension_academica.sql`](./extension_academica.sql) después del esquema base. Crea cursos, matrículas, notificaciones y las restricciones de equipo/entregas/tareas.
5. En **Table Editor**, confirma que existan `usuarios`, `cursos`, `matriculas`, `proyectos`, `proyecto_integrantes`, `entregables`, `tareas_kanban` y `notificaciones`.

El script activa Row Level Security. Los usuarios solo leen su perfil; estudiantes crean y consultan sus propios proyectos; profesores, coordinadores y administradores pueden consultar y revisar proyectos. La contraseña y la clave `service_role` no forman parte del SQL.

## 2. Crear las cuentas de acceso

En **Authentication > Users > Add user**, crea y confirma estas cuentas:

- `estudiante@sigpa.edu`
- `profesor@sigpa.edu`

Define las contraseñas en el panel de Supabase; no las guardes en el repositorio ni en el SQL. El trigger crea un perfil `Estudiante` automáticamente para cada cuenta.

Luego crea una consulta SQL nueva, copia todo [`bootstrap_profiles.sql`](./bootstrap_profiles.sql) y ejecútala. Esto repara perfiles cuya ID no coincide con `auth.users`, conserva sus referencias a proyectos/equipos y establece los roles.

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
La API solo ofrece al estudiante sus matrículas activas; no se deben conceder
permisos de lectura directa de esas tablas al navegador.

## 4. Configurar la aplicación local

En `sigpa-frontend/.env`, configura la URL y la clave **publishable** del proyecto:

```dotenv
VITE_SUPABASE_URL=https://<project-ref>.supabase.co
VITE_SUPABASE_ANON_KEY=<publishable-key>
```

Reinicia Vite después de cambiar `.env`. Ese archivo está excluido de Git. Nunca pongas una clave `service_role` o `sb_secret` en el frontend.

Si se usa el backend .NET, configura sus secretos fuera del repositorio, por ejemplo con `dotnet user-secrets` desde `SigpaBackend`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string-copied-from-Supabase-Connect>"
dotnet user-secrets set "Supabase:Url" "https://<project-ref>.supabase.co"
dotnet user-secrets set "Supabase:ServiceKey" "<service-role-key>"
```

Obtén la cadena de conexión desde **Project Settings > Database > Connect**. Para conexiones locales, usa el hostname/puerto que muestra el panel y la contraseña de base de datos actual; no sustituyas el hostname ni reutilices la clave `service_role` como contraseña de PostgreSQL.

## 5. Probar

1. Inicia el frontend y entra con cada cuenta de Supabase.
2. Como estudiante con matrícula activa, registra un proyecto, invita compañeros disponibles y confirma las filas en `public.proyectos`, `public.proyecto_integrantes` y `public.notificaciones`.
3. Como profesor, revisa el proyecto y cambia su estado.
4. Configura un hito pendiente y verifica que el estudiante pueda enviar un PDF/ZIP o enlace; prueba también el rechazo fuera de plazo.
5. Crea, asigna, mueve, edita y elimina tareas desde el tablero Kanban.
6. Cierra sesión y vuelve a entrar para comprobar que los cambios persisten.

Si el login autentica al usuario pero dice que no encuentra su perfil, ejecuta `bootstrap_profiles.sql`. Si aparece un error de columna o esquema al cargar proyectos, vuelve a ejecutar `schema.sql` completo y espera a que se recargue el esquema PostgREST. Si el login muestra un error de credenciales, confirma el usuario y la contraseña en Supabase Authentication.
