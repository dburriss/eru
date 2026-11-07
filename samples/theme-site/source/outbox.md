---
title: "ADR-014: Transactional outbox"
description: Why events are written to the outbox in the same transaction.
type: adr
tags: [messaging, adr]
---

# ADR-014: Transactional outbox

Write events to the outbox in the same transaction and relay them from a worker.
