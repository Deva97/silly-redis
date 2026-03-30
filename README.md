# silly-redis

An internal prototype inspired by Redis, built to understand and implement core concepts of a TCP-based key-value store.

## Overview

This project is a minimal Redis-like server that focuses on low-level networking, protocol parsing, and command handling.

## Implementation Approach

The implementation was developed incrementally by following a structured, hands-on approach.
Key reference:

* https://app.codecrafters.io/
* https://rohitpaulk.com/articles/redis-0

## Goals

* Understand TCP server design in .NET
* Implement basic RESP (Redis Serialization Protocol) parsing
* Handle simple commands like `PING`, `ECHO`, `SET`, `GET`
* Explore concurrency and async patterns