# Scenes

How a request travels, in three scenes.

## How a request travels

::: scene
% The title takes the top 19 units; shapes start below it.
box client 8 34 30 22 "Browser"
box api 65 34 30 22 "API" .accent step=1
ellipse db 122 34 30 22 "Database" .muted step=2
arrow client api "HTTPS" step=1
arrow api db step=2
text caption 8 70 144 8 "Every request passes the API." .small step=2
:::

::: notes
Start with the browser, then click twice: the API, then the database.
:::

## The API scales out {transition="morph"}

::: scene
box client 8 34 30 22 "Browser"
round gateway 50 40 22 10 "Gateway" .primary .bold
box api 65 24 30 14 "API 1" .accent
box api2 65 56 30 14 "API 2" .accent step=1 effect=fly
ellipse db 122 40 30 22 "Database" .dark
line client gateway
arrow gateway api
arrow gateway api2 name=to-second
arrow api db
arrow api2 db name=second-db
text caption 8 72 90 8 "A gateway spreads the load *and* keeps [one] address." .small .highlight exit=2
text note 100 72 52 8 "Two copies of the API." .small step=2
:::

## Status of each part {transition="morph"}

::: scene
round client 8 26 30 14 "Browser\nready" .success
round gateway 50 26 30 14 "Gateway\nin test" .info
round api 92 26 30 14 "API\nlate" .warning
round db 8 50 30 14 "Database\nblocked" .danger
round cache 50 50 30 14 "Cache\nnot started" .neutral
box legend 92 50 30 14 "Legend" .outline .large
text title 8 70 114 8 "Each colour is a status." .bold .primary
arrow client gateway
arrow gateway api
line db cache
:::
