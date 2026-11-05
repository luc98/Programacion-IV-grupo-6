# Historial de Prompts - Felipe Díaz

## Prompt 1
> /using-agent-skills  /plan Para empezar con este proyecto quiero que guardes en el archivo @[Prompts/Felipe_Diaz.md] cada prompt que yo (el usuario) voy a mandar en este proyecto.
> Ademas, quiero que hagas un artifact con la documentacion del proyecto a medida que avance.
> Este proyecto consiste en un Trabajo Practico de programacion que consiste en un sistema de reserva de asientos basado en microservicios. El archivo @[Trabajo_practico.pdf] contiene detalles del mismo y el analisis de requerimientos se encuentra en "C:\Users\Documents\DOCUMENTOS\UTN\2do Año\2 Cuatri\Programacion IV\Analisis.docx"
> El lenguaje principal de este proyecto es C# (.NET) e incluye materiales teoricos como gRPC, Authentication y Authorization, API REST, Microservicios, Tracing y Logging, Clean Architecture, Arquitectura de software N-Layer, Dominios anemicos y diagramas de arquitectura de software de cada API (Diagramas no tan complejos).
> El grupo consta de 8 integrantes en donde nos dividimos el trabajo en duplas para cada funcionalidad/parte del mismo.
> El desarrollo de mi dupla consiste en el microservicio de Booking REST API, el plan es @[Dupla2.md] .
> Quiero me ayudes a armar un artifact de implementacion del microservicio de Booking para alguien que no tiene tanta experiencia en C#, .NET ni en microservicios, gRPC y N-Layer Software Architecture

## Prompt 2
> 1. Vamos a utilizar carpetas dentro de BookingAPI.csproj
> 2. De momento los comentarios van a correr por cuenta propia
> 3. De momento deja en suspension la prueba local.
> Ademas, vamos a implementar PostreSQL para el Row Version / Timestamp con una columna que sea int Version. No vamos a implementar SQL Server con byte [].
> Los Logs de auditoria y Trazabilidad vav a ser implementado por la dupla 4 por lo que vamos a evitar poner eso en el codigo de momento (a menos que sea requerido mas adelante lo agregaremos), solo vamos a comentar en el codigo donde iria el mismo para ayudar.
> 
> Al implementation_plan.md quiero que me hagas una division del trabajo (lo mejor que puedas) para que tanto yo como mi dupla pueda hacer su parte del trabajo y que modifiques lo que se va a guardar de prompts en @[Prompts/Felipe_Diaz.md] , solo quiero que guardes el prompt enviado por mi y no el resumen tecnico generado

## Prompt 3
> Quiero me generes el script necesario para tener la imagen de PostgreSQL lista para docker y poder levantar un contenedor segun sea necesario


## Prompt 3
> Ya revisado el @[implementation_plan.md] actualizado, vamos con la implementacion de mi parte (ya complete los enums y Entidades del DAL)


## Prompt 4
> Por que para la capa DAL y @[BookingDbContext.cs] no utilizamos ApplyConfigurationFromAssembly ni un archivo IEntityTypeConfiguration<Tabla> ?
> Cual seria la diferencia a como ya tengo implementado DAL?


## Prompt 5
> En  @[ReservaRepository.cs] , el metodo CrearReservaRechazadaAsync y CrearReservaRechazadaAsync que se diferencian entre si ademas de que uno actualiza la Version de asiento (concurrencia optimista), ambos guardan la reserva en el context.
> En este caso, el CrearReservaRechazadaAsync lo que hace es guardar en el DbContext.Reserva una reserva y limpiar el ChangeTracker para activar la concurrencia


## Prompt 6
> En este caso, buscamos guardar en la DbContext la reserva rechazada para luego poder dar una respuesta clara a quien lo necesite (notification service)?


## Prompt 7
> /ponytail En @[BookingService.cs] , en Task ProcesarSolicitudReservaAsync no seria mejor en lugar de hacer la creacion de la entidad de Reserva, un metodo en @[ReservaRepository.cs] o algun servicio adicional que maneje la creacion? o generaria mucho codigo/abstraccion innecesaria?


## Prompt 8
> Claro, ademas no ponemos esta logica en la capa DAL debido a que es un dominio anemico tambien no?


## Prompt 9
> /ponytail-review /ponytail-audit Quiero revises mi parte de la feature-Booking-REST-API para ver si encontras algun error de logica, razonamiento o dependencias incorrectas en el codigo que se alineen con el paso 0 y el paso 1 (mi parte) de @[implementation_plan.md] 


## Prompt 10
> En el Must Fix, en el caso de hacer la validacion dentro de la misma llamada de guardado (ReservaRepository), deberia de tambier dejarla en el ProcesarSolicitudReservaAsync del BookingService? o es mucha redundancia?
> 
> Ya hice los demas cambios (revisalos por si cometi algun error en el codigo) y encadene el InvalidOperationException al catch en ProcesarSolicitudReservaAsync de@[BookingService.cs] . Siguiendo con este tema, deberia de agregar una columna extra a @[MotivoRechazo.cs] que sea algo como AsientoInvalido = 2 (y desplazo los demas 1 numero) de forma que lo use en este caso que trate de leer un asiento fantasma


## Prompt 11
> En este caso de negocio y de trabajo practico (se SUPONE que availability site verifica que el asiento existe antes de enviar la solicitud de reserva), deberia de sacar la excepcion?


## Prompt 11
> Hice unos cambios en @[BookingService.cs] @[IReservaRepository.cs] @[ReservaRepository.cs] de forma que: en los repository se cambio la firma del metodo GuardarReservaConcurrenteAsync agregando como parametro el asiento de forma que se puede manejar la condicion de carrera Check-Then-Act al guardarse en memoria el Asiento asi como la Reserva y ser el mismo Asiento el que se mande.
> Pasando que si: 
> Tanto la Solicitud A como la Solicitud B leen el asiento con Version = 1.
> Las dos pasan el if (asientoOcupado) porque ninguna guardó todavía.
> La Solicitud A llama a SaveChanges() primero:
> UPDATE "Asientos" SET "Version" = 2 WHERE "AsientoId" = @id AND "Version" = 1;
> (Afecta 1 fila: éxito).
> La Solicitud B llama a SaveChanges() un instante después. Como B partió de su entidad original con Version = 1, EF Core genera exactamente:
> UPDATE "Asientos" SET "Version" = 2 WHERE "AsientoId" = @id AND "Version" = 1;
> (Afecta 0 filas, porque en la base ahora vale 2).
> Entity Framework Core y PostgreSQL lanzan la DbUpdateConcurrencyException (sin necesidad de que yo haga throw new ...)
> El catch (DbUpdateConcurrencyException) en BookingService lo atrapa y rechaza a B.
> 
> Es este flujo con el codigo que tengo en mi BookingService correcto?
> Puede ser que haya un error en @ReservaRepository.cs en GuardarReservaConcurrenteAsync? al hacer asiento.Version++ me refiero


## Prompt 12
> Perfecto, ya hice los cambios minimos que me recomendaste (el indice y el comentario viejo), ahora si revisa por ultima vez y proba que compile correctamente y que los endpoints (Presentation) y los DAL funcione como deben.
> No modifiques codigo, solo mostrame sugerencias a cambiar en el mismo


## Prompt 13
> Empecemos entonces siguiendo trabajando desde nuestra rama, evitando lo mejor posible conflictos y temas relacionados con gRPC para no chocar con la rama de esa feature.
> Dejemos preparados los TraceId


## Prompt 14
> Hagamos la implementacion del @ServiceBusEventPublisher.cs como un HTTP POST hacia Notification Service ya que el profesor dijo que era solo conceptual pero al nosotros tenerlo ya implementado vamos a usarlo.
> Hagamos la implementacion con try/catch con un timeout corto que loguee un warning si notification service esta caido o responde con error pero que la reserva NO se cancele y el flujo de Booking pueda finalizar con exito.


