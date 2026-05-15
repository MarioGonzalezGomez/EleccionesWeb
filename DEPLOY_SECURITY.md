# Seguridad y despliegue (Fase 7)

## Autenticacion

- El acceso a pantallas operativas (`/operacion*`) requiere sesion.
- Login por cookie en `/login`.
- Logout en `/auth/logout`.

## Credenciales de operadores

- Configuracion en `OperatorAuthorization`.
- En produccion, usar `PasswordEnvVar` en lugar de `Password` plano.

Ejemplo:

```json
"OperatorAuthorization": {
  "AllowAllWhenEmpty": false,
  "Operators": [
    {
      "OperatorId": "admin",
      "Role": "admin",
      "PasswordEnvVar": "ELECCIONES_ADMIN_PASSWORD",
      "Modules": [ "Faldon", "Carton", "Superfaldon" ],
      "Permissions": []
    }
  ]
}
```

## Secretos de base de datos

- `Database:Password` puede ir vacio en `appsettings`.
- El backend toma `ELECCIONES_DB_PASSWORD` si no hay password configurado.

## Variables de entorno recomendadas

- `ELECCIONES_ADMIN_PASSWORD`
- `ELECCIONES_DB_PASSWORD`
- `ASPNETCORE_ENVIRONMENT=Production`

## Recomendaciones operativas

- Rotar contrasenas por evento electoral.
- No commitear secretos en `appsettings.*`.
- Restringir permisos de lectura al directorio de despliegue.
