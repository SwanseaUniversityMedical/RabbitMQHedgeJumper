# RabbitMQ Hedge Jumper
RabbitMQ site to Site Sync

Very similar to RabbitMQ Shovel (https://www.rabbitmq.com/docs/shovel) - but usign HTTPS and code that can be embedded into existing applications or deployed separatly


flowchart LR
    SRCQ[(RabbitMQ Source Queue)]
    SRCDLQ[(Source DLQ)]

    WORKER[Worker Service\n.NET Console App]

    KC[Keycloak\nOIDC Provider]

    API[REST API\n.NET Web API]

    EX[(RabbitMQ Topic Exchange)]
    DESTQ[(Destination Queue)]
    DESTDLQ[(Destination DLQ)]

    SRCQ --> WORKER
    WORKER -->|OAuth2 Client Credentials| KC
    WORKER -->|JWT Bearer| API
    API --> EX
    EX --> DESTQ

    SRCQ -. failed .-> SRCDLQ
    DESTQ -. failed .-> DESTDLQ
