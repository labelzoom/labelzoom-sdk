# R8 and ProGuard read this file from the jar automatically.
#
# JdkHttpTransport, the default transport, uses java.net.http, which Android does not have.
# Android apps pass their own HttpTransport, so JdkHttpTransport is never loaded there, but
# R8 still sees its references and fails a minified build on the missing classes.
-dontwarn java.net.http.**
