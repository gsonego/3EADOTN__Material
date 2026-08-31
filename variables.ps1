# All Azure resource-name variables used across the course, in one place.
# Run this once per terminal session to declare everything at once, instead of
# retyping each `$VAR = "..."` line individually as you reach it in a module's manual.
# Variables don't persist across terminal sessions — re-run this file whenever you
# start a new session (e.g. picking the course back up on a new day).
#
# This file is a convenience shortcut only. The manual's "define the variable right
# before its first use" convention (see module-revision-prd.md) is still what's taught
# step by step — keep both in sync as modules are added or resource names change.

# --- Module 1 ---
$LOCATION = "westcentralus"
$RG = "rg-estiam-dev-2"             # unique within your subscription
$PLAN = "asp-estiam-dev-2"
$ACR = "acrestiamdev2"              # must be globally unique across Azure
$WEBAPP = "webapp-estiam-dev-2"     # must be globally unique across Azure
$ENVNAME = "env-estiam-dev-2"
$APP = "app-estiam-dev-2"

# --- Module 2 ---
$COSMOS = "cosmos-estiam-dev-2"       # must be globally unique across Azure
$STORAGE = "stestiamdev2"             # must be globally unique across Azure

# --- Module 3 ---
$KV = "kv-estiam-dev-2"               # must be globally unique across Azure

# --- Module 4 ---
$LAW = "law-estiam-appi-dev-2"
$APPI = "appi-estiam-dev-2"

# --- Module 5 ---
$APIM = "apim-estiam-dev-2"           # must be globally unique across Azure
$EMAIL = "gsonego1@outlook.com"
$FUNC = "func-estiam-dev-2"           # must be globally unique across Azure
$FUNCLOCATION = "eastus"              # Function App compute only -- moved off westcentralus live, see manual Issues & Fixes

# --- Module 6 ---
$AOAILOCATION = "swedencentral"       # Azure OpenAI is not offered in westcentralus -- its own region, like $FUNCLOCATION
$AOAI         = "aoai-estiam-dev-2"   # must be globally unique across Azure (custom domain)
$AOAIDEPLOY   = "gpt-5-mini"          # the model deployment's name, used in the request URL

Write-Output "All variables declared."
Write-Output "--------------------------------"
Write-Output "LOCATION      ==> $LOCATION"
Write-Output "RG            ==> $RG"
Write-Output "PLAN          ==> $PLAN"
Write-Output "ACR           ==> $ACR"
Write-Output "WEBAPP        ==> $WEBAPP"
Write-Output "ENVNAME       ==> $ENVNAME"
Write-Output "APP           ==> $APP"
Write-Output "COSMOS        ==> $COSMOS"
Write-Output "STORAGE       ==> $STORAGE"
Write-Output "KV            ==> $KV"
Write-Output "LAW           ==> $LAW"
Write-Output "APPI          ==> $APPI"
Write-Output "APIM          ==> $APIM"
Write-Output "EMAIL         ==> $EMAIL"
Write-Output "FUNC          ==> $FUNC"
Write-Output "FUNCLOCATION  ==> $FUNCLOCATION"
Write-Output "AOAILOCATION  ==> $AOAILOCATION"
Write-Output "AOAI          ==> $AOAI"
Write-Output "AOAIDEPLOY    ==> $AOAIDEPLOY"
Write-Output "--------------------------------"