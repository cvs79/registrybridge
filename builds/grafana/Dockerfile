ARG VERSION
FROM index.docker.io/grafana/grafana-oss:${VERSION}

COPY ./content/conf/grafana.ini /etc/grafana/grafana.ini
COPY ./content/provisioning/ /etc/grafana/provisioning/
