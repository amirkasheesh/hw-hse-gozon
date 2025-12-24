# Gozon (ДЗ №4)

Микросервисная система для обработки заказов и платежей с гарантиями доставки сообщений (At-Least-Once), идемпотентностью и Real-time уведомлениями.

---

## Стек
- .NET 8 (Web API)
- EF Core + PostgreSQL (Npgsql)
- RabbitMQ (Native Client)
- YARP (Reverse Proxy)
- SignalR (WebSocket notifications)
- Docker / docker-compose

---

## Архитектура

Система состоит из 4 компонентов:

1) **ApiGateway** — единая точка входа.
- Маршрутизирует запросы к `OrdersService` и `PaymentsService`.
- Агрегирует Swagger документацию.
- Проксирует WebSocket соединения для SignalR.

2) **OrdersService** — управление заказами.
- При создании заказа сохраняет его в БД и атомарно создает запись в `Outbox` (Transactional Outbox).
- Фоновый процесс (Publisher) читает Outbox и отправляет сообщения в RabbitMQ с подтверждением доставки.
- Слушает очередь результатов оплаты и обновляет статус заказа.
- Рассылает Push-уведомления клиентам через SignalR при смене статуса.

3) **PaymentsService** — управление счетами и транзакциями.
- Реализует паттерн **Transactional Inbox**: дедупликация входящих сообщений для идемпотентности.
- Атомарно обновляет баланс пользователя и записывает результат в `Outbox`.
- Гарантирует, что деньги не спишутся дважды даже при дублировании сообщений.

4) **Frontend** — веб-клиент.
- Позволяет подписываться на обновления заказа по ID.
- Отображает статус оплаты в реальном времени.

---

## Запуск

В корне репозитория:

```bash
docker-compose up -d --build
```

После запуска:

* **Frontend**: `http://localhost:3000`
* **Gateway Swagger**: `http://localhost:5050/swagger`
* **RabbitMQ UI**: `http://localhost:15672` (guest/guest)

---

## Алгоритм обработки (как реализовано)

1. **Order Creation (Outbox):**
При вызове `POST /api/orders` сервис в одной транзакции сохраняет:
* Сам заказ (`Status = New`).
* Сообщение `InitiatePayment` в таблицу `outbox_messages`.


2. **Reliable Publishing:**
Фоновый воркер вычитывает сообщения из Outbox и отправляет их в RabbitMQ.
**Важно:** Используются `Publisher Confirms`. Сообщение помечается как отправленное (`ProcessedAtUtc`) только после получения `ack` от брокера. Это гарантирует **At-Least-Once**.
3. **Payment Processing (Inbox + Atomic Update):**
`PaymentsService` получает сообщение:
* Проверяет таблицу `inbox_messages`. Если `MessageId` уже был — игнорирует (идемпотентность).
* Если нет, пытается списать средства одним SQL-запросом:
```sql
UPDATE accounts SET balance = balance - X WHERE user_id = ... AND balance >= X
```


* Результат (Success/Fail) и событие для ответа записываются в одной транзакции.


4. **Completion:**
`OrdersService` получает результат оплаты, финально обновляет статус заказа (`Finished` / `Cancelled`) и отправляет уведомление в SignalR хаб.

Чтобы закончить сеанс:
```
docker-compose down -v
```

---

## API (через Gateway)

### POST `/accounts`

Создание счета для пользователя.
**Body:** `{ "userId": "string" }`

### POST `/accounts/{userId}/topup`

Пополнение баланса.
**Body:** `{ "amount": 1000 }`

### POST `/api/orders`

Создание заказа.
**Body:** `{ "userId": "string", "amount": 100 }`

### Real-time (WebSocket)

Подключение к хабу: `http://localhost:5050/hubs/orders`
Методы клиента:

* `JoinOrder(orderId)` — подписаться на изменения.
* Событие `OrderUpdated` — приходит при смене статуса.

---

## Устойчивость и арантии (на 10 баллов)

* **Падение RabbitMQ:** Сообщения остаются в таблицах `Outbox` и будут отправлены при восстановлении связи.
* **Падение Сервисов:** Благодаря `Durable Queues` и `Ack` сообщения не теряются, а ждут поднятия консьюмеров.
* **Дубликаты сообщений:** Механизм `Inbox` и проверка существования операций в БД предотвращают повторное списание денег (Effectively Exactly-Once).

