## Scenes

How a request travels, in three scenes.

## How a request travels

Browser

API

Database

Every request passes the API.

HTTPS

::: notes
Start with the browser, then click twice: the API, then the database.
:::

## The API scales out

Browser

Gateway

API 1

API 2

Database

A gateway spreads the load \*and\* keeps \[one] address.

Two copies of the API.

## Status of each part

Browser ready

Gateway in test

API late

Database blocked

Cache not started

Legend

Each colour is a status.
