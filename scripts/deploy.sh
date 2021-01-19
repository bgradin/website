#! /bin/bash

cd public
find . \
  -wholename '*' \
  -printf '%p\n' \
  -type f \
  -exec curl -sk sftp://${SFTP_HOST}:${SFTP_PORT}/~/html/{} -u ${SFTP_USER}:${SFTP_PASSWORD} -T {} --ftp-create-dirs\;