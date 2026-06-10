# test_add_vehicle.ps1
Add-Type -AssemblyName System.Net.Http

# Get JWT token
$loginBody = @{
    username = "superadmin"
    password = "password123"
} | ConvertTo-Json

$loginResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResponse.data.token
Write-Host "Logged in. Token: $token"

# Prepare multipart form content
$multipartContent = [System.Net.Http.MultipartFormDataContent]::new()

# Helper function to add string content
function Add-StringField($name, $value) {
    $stringContent = [System.Net.Http.StringContent]::new($value)
    $multipartContent.Add($stringContent, $name)
}

# Helper function to add file content
function Add-FileField($name, $fileName, $contentType, $bytes) {
    $byteArrayContent = [System.Net.Http.ByteArrayContent]::new($bytes)
    $byteArrayContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($contentType)
    $multipartContent.Add($byteArrayContent, $name, $fileName)
}

Add-StringField -name "vehicleNumber" -value "HR26AB1111"
Add-StringField -name "vehicleType" -value "Petroleum Tanker"
Add-StringField -name "driverName" -value "Test Driver"
Add-StringField -name "vendorName" -value "Test Vendor"
Add-StringField -name "departmentId" -value "1" # PR-FS department id

# Add RC copy dummy file
$dummyBytes = [System.Text.Encoding]::UTF8.GetBytes("Dummy PDF content for RC")
Add-FileField -name "doc_RC" -fileName "rc.pdf" -contentType "application/pdf" -bytes $dummyBytes

# Add 8 compliance documents
$complianceTypes = @("ROAD_PERMIT", "AGE_DETERMINATION", "PUC", "FITNESS", "EXPLOSIVE", "GREEN_CARD", "INSURANCE", "CALIBRATION")
foreach ($type in $complianceTypes) {
    $fileBytes = [System.Text.Encoding]::UTF8.GetBytes("Dummy content for $type")
    Add-FileField -name "doc_$type" -fileName "$($type.ToLower()).pdf" -contentType "application/pdf" -bytes $fileBytes
    Add-StringField -name "expiry_$type" -value "2027-12-31"
    Add-StringField -name "licNo_$type" -value "LIC-$type-12345"
}

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)

try {
    $response = $httpClient.PostAsync("http://localhost:5000/api/vehicles", $multipartContent).Result
    $status = $response.StatusCode
    $body = $response.Content.ReadAsStringAsync().Result
    Write-Host "Response Status: $status"
    Write-Host "Response Body: $body"
} catch {
    Write-Error $_
}
