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
* Handle simple commands like `PING`, `ECHO`, `SET`, `GET`, `RPUSH`, `LPUSH`, `LRANGE`, `LLEN`, `LPOP`
* Explore concurrency and async patterns

## Implemented Commands

### Key-Value Operations
* `PING` - Server ping
* `ECHO` - Echo the given string
* `SET` - Set the string value of a key (with optional TTL: `EX` for seconds or `PX` for milliseconds)
* `GET` - Get the value of a key

### List Operations
* `RPUSH` - Append one or more values to a list (right push)
* `LPUSH` - Prepend one or more values to a list (left push)
* `LRANGE` - Get a range of elements from a list
* `LLEN` - Get the length of a list
* `LPOP` - Remove and get the first N elements from a list