---
title: BeginDbTransaction
---

# MySqlConnection.BeginDbTransaction method

Begins a database transaction.

```csharp
protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
```

| parameter | description |
| --- | --- |
| isolationLevel | The IsolationLevel for the transaction. |

## Return Value

A [`MySqlTransaction`](../../MySqlTransactionType/) representing the new database transaction.

## See Also

* class [MySqlConnection](../../MySqlConnectionType/)
* namespace [MySqlConnector](../../MySqlConnectionType/)
* assembly [MySqlConnector](../../../MySqlConnectorAssembly/)

<!-- DO NOT EDIT: generated by xmldocmd for MySqlConnector.dll -->